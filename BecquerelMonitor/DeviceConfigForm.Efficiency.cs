using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    /// <summary>
    /// Вкладка «Эффективность» конфигурации прибора.
    ///
    /// Кривая привязана к ПРИБОРУ И ГЕОМЕТРИИ: эффективность полного поглощения
    /// зависит от телесного угла и самопоглощения в пробе, поэтому один и тот же
    /// кристалл в маринелли, в банке и с точечным источником даёт три разные
    /// кривые. Отсюда список: конфигураций у прибора много, действует одна.
    ///
    /// Вкладка построена кодом, а не конструктором форм: строки берутся из общих
    /// ресурсов, где у них уже есть русская пара, и правка не требует трогать
    /// resx формы на четыре тысячи строк.
    /// </summary>
    public partial class DeviceConfigForm
    {
        TabPage efficiencyTabPage;
        ComboBox efficiencyCombo;
        GeometrySketch efficiencySketch;
        Label efficiencySummaryLabel;
        Panel efficiencyHeader;
        Button efficiencyNewButton, efficiencyEditButton, efficiencyRenameButton;
        Button efficiencyDuplicateButton, efficiencyDeleteButton, efficiencyMatrixButton;

        /// <summary>
        /// Подпись о расхождении поколений расчёта (`A119`) — своя, а не хвост
        /// к сводке: её высота меняется с текстом и с языком. Пуста и невидима,
        /// пока поколения кривой, матрицы и сборки сходятся.
        /// </summary>
        Label efficiencyGenerationLabel;

        /// <summary>Высота шапки БЕЗ подписи о поколениях — от неё считается рост.</summary>
        int efficiencyHeaderBaseHeight;

        /// <summary>Просвет между сводкой и подписью о поколениях.</summary>
        const int EfficiencyGenerationGap = 4;

        /// <summary>
        /// Импорт текстового экспорта ЛСРМ (`EffCalcMC.txt`).
        ///
        /// ⛔ `AMBER13`, решение Amber 10.09.2026 «Снять и завести ввоз на
        /// Efficiency». До того импорт жил на вкладке `DoseRate` кнопкой
        /// `buttonLoadEff`, и это была ЕДИНСТВЕННАЯ дверь импорта ЛСРМ во всём
        /// приложении — а импортированная кривая никуда не сохранялась: она лежала в
        /// поле формы (`doseRateFileCurve`) и пропадала вместе с окном. Здесь
        /// она становится обычной <see cref="EfficiencyConfigData"/> в списке
        /// прибора, то есть переживает закрытие окна, «Сохранить» и снимок в
        /// файл спектра.
        /// </summary>
        Button efficiencyImportButton;

        /// <summary>
        /// Собрать вкладку и вставить её СРАЗУ ЗА калибровкой энергии: кривая —
        /// это тоже градуировка прибора, и стоять ей рядом с остальными.
        /// </summary>
        void BuildEfficiencyTab()
        {
            this.efficiencyTabPage = new TabPage
            {
                Text = Resources.DeviceConfigEfficiencyTab,
                UseVisualStyleBackColor = true,
                Padding = new Padding(3),
            };

            // Ширина вкладки НЕ РАСТЯГИВАЕТСЯ: tabControl1 привязан Top|Bottom|
            // Right, то есть с окном меняется только высота, а страница всегда
            // 490 точек. Раскладка считается от этого числа: шесть кнопок в один
            // ряд не помещаются даже впритык, поэтому два ряда по три.
            const int Margin = 12;
            const int Width = 490 - 2 * Margin;   // 466
            const int Gap = 8;
            const int ButtonWidth = (Width - 2 * Gap) / 3;   // 150

            // Шапка и чертёж разложены доком, а не якорями. Якоря считают
            // растяжение от размера, который у страницы НА МОМЕНТ СБОРКИ ещё не
            // тот: она получает свои 490x599 позже, и растянутый на разницу
            // чертёж уезжал за край (768x1086 при поле 490x599). Док от
            // размера не зависит вовсе.
            //
            // ⛔ Высота шапки — ЧИСЛО, и оно устаревает при первой же
            // добавленной строке: панель обрезает детей МОЛЧА, без исключения
            // и без признака. Третий ряд кнопок (импорт ЛСРМ, `AMBER13`) прибавил
            // 32 точки, и высота выросла ровно на них: 166 → 198.
            this.efficiencyHeader = new Panel { Dock = DockStyle.Top, Height = 198 };
            Panel header = this.efficiencyHeader;

            int y = Margin;
            this.efficiencyNewButton = this.EfficiencyButton(
                Resources.EfficiencyTabNew, Margin, y, ButtonWidth);
            this.efficiencyEditButton = this.EfficiencyButton(
                Resources.EfficiencyTabEdit, Margin + ButtonWidth + Gap, y, ButtonWidth);
            this.efficiencyRenameButton = this.EfficiencyButton(
                Resources.EfficiencyTabRename, Margin + 2 * (ButtonWidth + Gap), y, ButtonWidth);

            y += 32;
            this.efficiencyDuplicateButton = this.EfficiencyButton(
                Resources.EfficiencyTabDuplicate, Margin, y, ButtonWidth);
            this.efficiencyDeleteButton = this.EfficiencyButton(
                Resources.EfficiencyTabDelete, Margin + ButtonWidth + Gap, y, ButtonWidth);
            this.efficiencyMatrixButton = this.EfficiencyButton(
                Resources.EfficiencyTabResponseMatrix, Margin + 2 * (ButtonWidth + Gap), y, ButtonWidth);

            // Третий ряд: импорт экспорта ЛСРМ (`AMBER13`, решение Amber
            // 10.09.2026). Стоит ОТДЕЛЬНО от шести кнопок правки, а не седьмой
            // в их ряду: те шесть работают с ВЫБРАННОЙ кривой, а эта заводит
            // новую и выбора не требует вовсе.
            y += 32;
            this.efficiencyImportButton = this.EfficiencyButton(
                Resources.EfficiencyTabImportLsrm, Margin, y, ButtonWidth);

            this.efficiencyNewButton.Click += this.efficiencyNewButton_Click;
            this.efficiencyEditButton.Click += this.efficiencyEditButton_Click;
            this.efficiencyRenameButton.Click += this.efficiencyRenameButton_Click;
            this.efficiencyDuplicateButton.Click += this.efficiencyDuplicateButton_Click;
            this.efficiencyDeleteButton.Click += this.efficiencyDeleteButton_Click;
            this.efficiencyMatrixButton.Click += this.efficiencyMatrixButton_Click;
            this.efficiencyImportButton.Click += this.efficiencyImportButton_Click;

            // Подпись отдельной строкой над списком, а не слева от него:
            // «Конфигурация эффективности:» съедает треть ширины, и списку
            // остаётся меньше, чем нужно на имя файла кривой.
            y += 38;
            header.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(Margin, y),
                Text = Resources.EfficiencyTabList,
            });

            y += 18;
            this.efficiencyCombo = new ComboBox
            {
                Location = new Point(Margin, y),
                Size = new Size(Width, 21),
                DropDownStyle = ComboBoxStyle.DropDownList,
            };

            this.efficiencyCombo.SelectedIndexChanged += this.efficiencyCombo_SelectedIndexChanged;
            header.Controls.Add(this.efficiencyCombo);

            y += 28;
            this.efficiencySummaryLabel = new Label
            {
                AutoSize = false,
                Location = new Point(Margin, y),
                Size = new Size(Width, 30),
                ForeColor = Color.DimGray,
            };

            header.Controls.Add(this.efficiencySummaryLabel);

            // Подпись о расхождении поколений (`A119`) — под сводкой, своим
            // цветом и своей высотой. Заводится пустой и невидимой: у сцены, где
            // кривая, матрица и сборка одного поколения, сказать нечего, и
            // пустая строка не должна отнимать у чертежа ни точки.
            y += 30 + EfficiencyGenerationGap;
            this.efficiencyGenerationLabel = new Label
            {
                AutoSize = false,
                Location = new Point(Margin, y),
                Size = new Size(Width, 0),
                ForeColor = Color.Firebrick,
                Visible = false,
            };

            header.Controls.Add(this.efficiencyGenerationLabel);
            this.efficiencyHeaderBaseHeight = header.Height;

            // Чертёж забирает всё, что осталось под шапкой: высота с окном
            // растёт, ширина нет.
            this.efficiencySketch = new GeometrySketch
            {
                Mode = GeometrySketch.SketchMode.Overview,
                Dock = DockStyle.Fill,
                Margin = new Padding(Margin),
            };

            Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Margin, 0, Margin, Margin) };
            body.Controls.Add(this.efficiencySketch);

            // Заполняющий добавляется ПЕРВЫМ, прижатый сверху — вторым: док
            // разбирается от старших индексов к младшим, и шапка обязана занять
            // своё место раньше, чем остаток отдадут чертежу.
            this.efficiencyTabPage.Controls.Add(body);
            this.efficiencyTabPage.Controls.Add(header);

            // Вставка именно за калибровкой энергии. Индекс ищется, а не пишется
            // числом: порядок вкладок в конструкторе меняют, и жёсткая четвёрка
            // однажды поставила бы вкладку не туда молча.
            //
            // Вставляется НЕ через TabPages.Insert. Он до создания дескриптора
            // окна кладёт страницу только в Controls, а в TabPages она не
            // попадает: получается семь контролов против шести вкладок, и
            // вкладки на форме нет вовсе. Ошибка тихая — ни исключения, ни
            // предупреждения, страница по всем признакам «создана и
            // родительская привязка есть».
            //
            // Add и Remove этим не страдают (ими же пользуется
            // HideTempcoTabPage), поэтому хвост снимается и возвращается на
            // место следом за новой вкладкой.
            int index = this.tabControl1.TabPages.IndexOf(this.tabPage2);
            if (index < 0)
            {
                this.tabControl1.TabPages.Add(this.efficiencyTabPage);
                return;
            }

            List<TabPage> tail = new List<TabPage>();
            for (int i = this.tabControl1.TabPages.Count - 1; i > index; i--)
            {
                tail.Insert(0, this.tabControl1.TabPages[i]);
                this.tabControl1.TabPages.RemoveAt(i);
            }

            this.tabControl1.TabPages.Add(this.efficiencyTabPage);
            foreach (TabPage page in tail)
            {
                this.tabControl1.TabPages.Add(page);
            }
        }

        Button EfficiencyButton(string text, int x, int y, int width)
        {
            Button button = new Button
            {
                Location = new Point(x, y),
                Size = new Size(width, 26),
                Text = text,
                UseVisualStyleBackColor = true,
            };

            this.efficiencyHeader.Controls.Add(button);
            return button;
        }

        // ------------------------------------------------------------------
        // Загрузка и сохранение
        // ------------------------------------------------------------------

        /// <summary>
        /// Наполнить список конфигурациями прибора и выбрать действующую.
        /// </summary>
        void LoadEfficiencyTab(DeviceConfigInfo config)
        {
            this.efficiencyCombo.Items.Clear();
            this.efficiencyCombo.Items.Add(Resources.EfficiencyTabNone);
            int selected = 0;
            if (config != null && config.EfficiencyConfigs != null)
            {
                foreach (EfficiencyConfigData item in config.EfficiencyConfigs)
                {
                    int i = this.efficiencyCombo.Items.Add(item);
                    if (item.Guid == config.ActiveEfficiencyGuid)
                    {
                        selected = i;
                    }
                }
            }

            this.efficiencyCombo.SelectedIndex = selected;
            this.UpdateEfficiencyView();
        }

        /// <summary>
        /// Из полей формы в конфигурацию попадает только ВЫБОР действующей:
        /// сам список правится кнопками сразу, на месте, потому что «создать» и
        /// «удалить» — это действия, а не редактируемые значения.
        /// </summary>
        void SaveEfficiencyTab(DeviceConfigInfo config)
        {
            if (config == null)
            {
                return;
            }

            EfficiencyConfigData selected = this.SelectedEfficiency();
            config.ActiveEfficiencyGuid = selected == null ? null : selected.Guid;
        }

        EfficiencyConfigData SelectedEfficiency()
        {
            return this.efficiencyCombo == null
                ? null
                : this.efficiencyCombo.SelectedItem as EfficiencyConfigData;
        }

        /// <summary>
        /// Перерисовать миниатюру и подпись под списком. Подпись обязана
        /// называть ровно то, что мешает: «конфигураций нет» и «кривая без
        /// геометрии» — разные беды с разным лечением.
        /// </summary>
        /// <summary>
        /// Матрица отклика для геометрии выбранной кривой. Форма работает с той
        /// же копией конфигурации, что и вкладка, поэтому геометрия в ней —
        /// ровно та, что человек видит в чертеже.
        /// </summary>
        void efficiencyMatrixButton_Click(object sender, EventArgs e)
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            if (config == null || !config.HasGeometry)
            {
                return;
            }

            using (ResponseMatrixForm form = new ResponseMatrixForm(config))
            {
                // `AMBER48`: подпись поколений и сводка вкладки читаются из
                // ЗАГОЛОВКА ФАЙЛА склада при каждом `UpdateEfficiencyView()`,
                // и больше ниоткуда. Запись матрицы меняет файл, а вкладку не
                // трогала — «Curve is generation 19, its response matrix is
                // generation 18» стояло после пересчёта и записи, пока не
                // сменишь кривую или не откроешь окно заново (Amber
                // 18.09.2026, три снимка). Освежаем при записи — вкладка видна
                // за модальным окном — и ещё раз по закрытию, на любой исход.
                form.MatrixSaved += this.responseMatrixForm_MatrixSaved;
                form.ShowDialog(this);
                // Выключатель матрицы (W11) пишет в ту же копию конфигурации —
                // осталось пометить её изменённой, чтобы «Сохранить» ожило.
                // (`AMBER186`) Записанная окном матрица лежит во ВРЕМЕННОМ
                // файле и уйдёт на склад только сохранением конфигурации —
                // значит, конфигурация изменена, и вопрос «сохранить?» обязан
                // прозвучать: «Да» перенесёт матрицу, «Нет» снимет её.
                if (form.UseMatrixTouched || form.MatrixSavedPending)
                {
                    this.SetActiveDeviceConfigDirty();
                }
            }

            this.UpdateEfficiencyView();
        }

        void responseMatrixForm_MatrixSaved(object sender, EventArgs e)
        {
            this.UpdateEfficiencyView();
        }

        // ------------------------------------------------------------------
        // Матрицы отклика при сохранении конфигурации (`AMBER186`)
        // ------------------------------------------------------------------

        /// <summary>
        /// (`AMBER186`) Guid кривых конфигурации в том виде, в каком она лежит
        /// у менеджера (то есть на диске), — снимок ДО записи: запись подменяет
        /// запись менеджера клоном, и после неё «какие кривые были» не узнать.
        /// </summary>
        List<string> SavedEfficiencyGuids(DeviceConfigInfo config)
        {
            List<string> guids = new List<string>();
            DeviceConfigInfo saved;
            if (config != null && !string.IsNullOrEmpty(config.Guid)
                && this.manager.DeviceConfigMap.TryGetValue(config.Guid, out saved)
                && saved != null && saved.EfficiencyConfigs != null)
            {
                foreach (EfficiencyConfigData item in saved.EfficiencyConfigs)
                {
                    guids.Add(item.Guid);
                }
            }

            return guids;
        }

        static List<string> EfficiencyGuids(DeviceConfigInfo config)
        {
            List<string> guids = new List<string>();
            if (config != null && config.EfficiencyConfigs != null)
            {
                foreach (EfficiencyConfigData item in config.EfficiencyConfigs)
                {
                    guids.Add(item.Guid);
                }
            }

            return guids;
        }

        /// <summary>
        /// (`AMBER186`, решение Amber 05.10.2026 «Временный файл + перенос»)
        /// Конфигурация ЗАПИСАНА — перенести на склад матрицы, записанные окном
        /// матрицы во временные файлы, и снять со склада матрицы кривых,
        /// удалённых из конфигурации (кроме тех, чей Guid остался у другой
        /// конфигурации прибора — копия конфигурации делит с прежней Guid кривых
        /// и файл склада). Зовётся только ПОСЛЕ удачной записи.
        /// </summary>
        void CommitResponseMatrices(DeviceConfigInfo saved, List<string> guidsBefore)
        {
            List<string> now = EfficiencyGuids(saved);
            List<string> failures = ResponseMatrixStore.CommitPending(now);

            List<string> removed = new List<string>();
            foreach (string guid in guidsBefore ?? new List<string>())
            {
                if (!now.Contains(guid))
                {
                    removed.Add(guid);
                }
            }

            if (removed.Count > 0)
            {
                HashSet<string> referenced = new HashSet<string>(StringComparer.Ordinal);
                foreach (DeviceConfigInfo other in this.manager.DeviceConfigList)
                {
                    foreach (string guid in EfficiencyGuids(other))
                    {
                        referenced.Add(guid);
                    }
                }

                ResponseMatrixStore.DeleteRemoved(removed, referenced);
            }

            if (failures.Count > 0)
            {
                AppUi.Report(string.Format(CultureInfo.InvariantCulture,
                                           Resources.ResponseMatrixCommitFailed,
                                           string.Join(Environment.NewLine, failures.ToArray())),
                             Resources.ResponseMatrixTitle, MessageBoxIcon.Warning);
            }
        }

        void UpdateEfficiencyView()
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            bool has = this.activeDeviceConfig != null
                       && this.activeDeviceConfig.EfficiencyConfigs.Count > 0;

            // «Изменить» доступно и без геометрии: это единственный способ её
            // ДОПИСАТЬ. У кривой, восстановленной по измерениям, геометрии нет,
            // и запертая кнопка оставляла такую кривую навсегда непересчитываемой.
            this.efficiencyEditButton.Enabled = config != null;
            this.efficiencyRenameButton.Enabled = config != null;
            this.efficiencyDuplicateButton.Enabled = config != null;
            this.efficiencyDeleteButton.Enabled = config != null;
            // Матрица считается ИЗ ГЕОМЕТРИИ: у кривой, восстановленной по
            // измерениям, её нет, и считать не из чего.
            this.efficiencyMatrixButton.Enabled = config != null && config.HasGeometry;

            if (config == null)
            {
                this.efficiencySummaryLabel.Text = has ? "" : Resources.EfficiencyTabEmpty;
                this.ShowGenerationNotes(null);
                this.efficiencySketch.SetModel(null);
                return;
            }

            List<string> parts = new List<string>();
            if (config.HasCurve)
            {
                // `A244`: числа в подписи — инвариантом. Точка у дробной части
                // и никакой группировки разрядов (решение Amber 05.09.2026).
                parts.Add(string.Format(CultureInfo.InvariantCulture, Resources.EfficiencyTabSummary,
                                        config.Curve.Count,
                                        (int)config.Curve[0].Energy,
                                        (int)config.Curve[config.Curve.Count - 1].Energy));
            }

            if (!config.HasGeometry)
            {
                parts.Add(Resources.EfficiencyTabNoGeometry);
            }

            // Клеймо «чем посчитана» (E12) — прямым текстом: инвариантная
            // строка из конфигурации, по ней кривые разной физики различимы
            // на глаз. Пустое клеймо не показывается — у измерительной и
            // ручной кривой его нет по смыслу.
            if (!string.IsNullOrEmpty(config.ComputeStamp))
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture,
                                        Resources.EfficiencyTabComputeStamp, config.ComputeStamp));
            }

            // ⛔ (`A119`) ПОКОЛЕНИЕ КРИВОЙ НАЗЫВАЕТСЯ СЛОВАМИ, А НЕ ОДНИМ
            // КЛЕЙМОМ. Клеймо выше показывает `phys=11` — и это ровно столько
            // же, сколько ничего: с чем сравнивать `11`, человек за экраном не
            // знает, номер поколения переноса нигде больше не показан. Замер
            // склада 04.09.2026 (`A50`) и повторный 11.09.2026 нашли в ОДНОЙ
            // сцене кривую и матрицу разных поколений, и увидеть это можно было
            // только чтением клейм двоичного файла отдельной пробой.
            //
            // Матрица про себя такое говорит с 05.09.2026 (~~`A50`~~, форма
            // «Матрица отклика» разводит пять отказов), кривая — не говорила.
            int matrixPhysics = 0;
            int matrixFormat;
            // (`AMBER186`) Матрица, ждущая сохранения конфигурации, новее
            // склада — поколение говорится о ней (подпись `AMBER48` уходит
            // сразу после записи окном, как и прежде).
            if (!ResponseMatrixStore.PeekVersions(config.Guid, ResponseMatrixStore.EditingSource(config.Guid),
                                                  out matrixFormat, out matrixPhysics))
            {
                matrixPhysics = 0;      // матрицы нет или файл не наш — сравнивать не с чем
            }

            this.efficiencySummaryLabel.Text = string.Join("   ", parts.ToArray());

            // ⛔ ОТДЕЛЬНОЙ ПОДПИСЬЮ, А НЕ ХВОСТОМ К СВОДКЕ. Сводка живёт в
            // подписи 466×30 с `AutoSize = false`, и в ней уже две строки:
            // «точек кривой …» плюс клеймо. Дописанные к ней два предложения
            // ушли бы за нижний край МОЛЧА — панель обрезает детей без
            // исключения и без признака, — и починка `A119` выглядела бы
            // сделанной, оставаясь невидимой.
            // (`S208`, П216) Четвёртый довод — обязана ли кривая этой геометрии
            // нести кусок пика окном (`peps=fwhm`): у геометрии с разрешением.
            // (`AMBER185`, `AMBER200`) Вторым списком — что сказать о ГЕОМЕТРИИ
            // кривой (<see cref="GeometryNotes"/>), той же подписью.
            this.ShowGenerationNotes(GenerationNotes(config.ComputeStamp, matrixPhysics,
                                                     ResponseMatrix.PhysicsVersion,
                                                     EfficiencyCalculation.PeakWindowExpected(config.Geometry)),
                                     GeometryNotes(config));
            this.efficiencySketch.SetModel(config.Geometry);
        }

        /// <summary>
        /// Что сказать о ГЕОМЕТРИИ выбранной кривой — той же подписью, что
        /// поколения (<see cref="ShowGenerationNotes"/>), и тоже без окна:
        ///
        ///   * (`AMBER185`, решение Amber 05.10.2026 «Отпечаток + предупреждение»)
        ///     кривая посчитана для другой геометрии — отпечаток при кривой не
        ///     сходится с геометрией рядом (<see cref="EfficiencyConfigData.CurveGeometryMismatch"/>);
        ///   * (`AMBER200`) слой с толщиной без вещества — проверка слоёв
        ///     модели КАК ОНА ЕСТЬ (<see cref="GeometryModel.Warnings"/>): у
        ///     геометрии из конфигурации она прежде не звалась вовсе, и слот
        ///     уходил в счёт пустотой молча.
        /// </summary>
        internal static List<string> GeometryNotes(EfficiencyConfigData config)
        {
            List<string> notes = new List<string>();
            if (config == null || !config.HasGeometry)
            {
                return notes;
            }

            if (config.CurveGeometryMismatch)
            {
                notes.Add(Resources.EfficiencyCurveOtherGeometry);
            }

            notes.AddRange(config.Geometry.Warnings);
            return notes;
        }

        /// <summary>
        /// Показать (или убрать) подпись о расхождении поколений.
        ///
        /// ⛔ ВЫСОТА СЧИТАЕТСЯ, А НЕ ПИШЕТСЯ ЧИСЛОМ. Высота числом устаревает
        /// при первой же добавленной строке и при первом же переводе, который
        /// длиннее английского: русская пара обеих подписей длиннее на треть.
        /// Здесь высота — <see cref="GenerationLabelHeight"/> от настоящего
        /// текста при настоящей ширине, и вместе с ней растёт шапка вкладки;
        /// чертёж под ней доковый и подвинется сам.
        /// </summary>
        void ShowGenerationNotes(List<string> notes, List<string> geometryNotes = null)
        {
            List<string> all = new List<string>();
            if (notes != null)
            {
                all.AddRange(notes);
            }

            if (geometryNotes != null)
            {
                all.AddRange(geometryNotes);
            }

            string text = all.Count == 0
                ? ""
                : string.Join(Environment.NewLine, all.ToArray());
            Label label = this.efficiencyGenerationLabel;
            label.Text = text;
            int need = GenerationLabelHeight(text, label.Font, label.Width);
            label.Height = need;
            label.Visible = need > 0;
            this.efficiencyHeader.Height = this.efficiencyHeaderBaseHeight
                                           + (need > 0 ? need + EfficiencyGenerationGap : 0);
        }

        /// <summary>
        /// Сколько точек по высоте занимает текст подписи при данной ширине.
        /// Пустой текст — ноль: подписи нет вовсе, и шапка не растёт.
        ///
        /// Вынесено отдельным приёмом нарочно: так высоту меряет безоконная
        /// проба (`CurveGenerationProbe`), а не снимок формы.
        /// </summary>
        internal static int GenerationLabelHeight(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0)
            {
                return 0;
            }

            return TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue),
                                            TextFormatFlags.WordBreak).Height;
        }

        /// <summary>
        /// ⛔ (`A119`) РАЗНЫЕ ПОКОЛЕНИЯ РАСЧЁТА, ЛЕЖАЩИЕ РЯДОМ, — СЛОВАМИ.
        ///
        /// Поколений в одной сцене ТРИ, и расходиться они умеют независимо:
        ///
        ///   * поколение КРИВОЙ — `phys=N` в её клейме
        ///     (<see cref="EfficiencyConfigData.ComputeStamp"/>);
        ///   * поколение МАТРИЦЫ — `phys=N` в клейме её файла, склад
        ///     <see cref="ResponseMatrixStore"/>;
        ///   * поколение СБОРКИ — <see cref="ResponseMatrix.PhysicsVersion"/>.
        ///
        /// Матрица пересчитывается сама (её клеймо перестаёт сходиться при смене
        /// физики), кривая — НЕТ: она лежит в конфигурации прибора числами и
        /// переживает любое поколение переноса. Отсюда и дефект: пересчёт одних
        /// матриц оставляет рядом кривую прежнего поколения, и это не видно
        /// ниоткуда, кроме клейм.
        ///
        /// ⚠ Метод СТАТИЧЕСКИЙ и чистый нарочно: решение о том, что сказать,
        /// не должно требовать окна — так его меряет безоконная проба
        /// (`CurveGenerationProbe`), а не снимок подписи.
        ///
        /// Клеймо без `phys=` (кривая по измерениям, ручная, или посчитанная до
        /// заведения клейм) молчит: сказать про неё нечего, а «поколение 0»
        /// было бы неправдой.
        /// </summary>
        /// <param name="computeStamp">клеймо кривой; пустое — молчим</param>
        /// <param name="matrixPhysics">поколение матрицы склада; 0 — матрицы нет</param>
        /// <param name="buildPhysics">поколение этой сборки</param>
        /// <param name="peakWindowExpected">(`S208`, П216) обязана ли кривая нести кусок
        /// пика окном <see cref="EfficiencyCalculation.PeakWindowStamp"/> — у геометрии с
        /// разрешением (<see cref="EfficiencyCalculation.PeakWindowExpected"/>)</param>
        internal static List<string> GenerationNotes(string computeStamp, int matrixPhysics,
                                                     int buildPhysics, bool peakWindowExpected)
        {
            List<string> notes = new List<string>();
            int curvePhysics = ResponseMatrix.PhysicsFromStamp(computeStamp);
            if (curvePhysics <= 0)
            {
                return notes;
            }

            // `A244`: числа — инвариантной культурой, точка и никакой
            // группировки разрядов (решение Amber 05.09.2026).
            if (curvePhysics != buildPhysics)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                                        Resources.EfficiencyTabCurveOldPhysics,
                                        curvePhysics, buildPhysics));
            }
            else if (peakWindowExpected
                     && computeStamp.IndexOf(EfficiencyCalculation.PeakWindowStamp,
                                             StringComparison.Ordinal) < 0)
            {
                // ⛔ (`S208`, П216; решение Amber 02.10.2026 «Научить вкладку и
                // сторожа peps=fwhm») ПОКОЛЕНИЕ ТО ЖЕ, ОПРЕДЕЛЕНИЕ ПИКА ПРЕЖНЕЕ.
                // Пик кривой с физики 26 считается окном полной строки ±ПШПВ/2
                // (кусок клейма `peps=fwhm`), а номер `phys=` у кривой общий с
                // матрицей и поднят не был — без этой фразы кривая, посчитанная
                // до правки, выглядела бы нынешней. Только у геометрии с
                // разрешением: без него окна нет, и кривая прежняя по праву.
                // При отставшей физике фраза не нужна — «пересчитайте» уже
                // сказано строкой выше.
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                                        Resources.EfficiencyTabCurveOldPeak,
                                        curvePhysics, EfficiencyCalculation.PeakWindowStamp));
            }

            // Сравнение с матрицей — ОТДЕЛЬНОЕ: кривая и матрица бывают обе
            // старыми, но одного поколения (тогда сказать надо одно), и бывают
            // разного (тогда два). Матрица нулём — её нет, и молчим.
            if (matrixPhysics > 0 && matrixPhysics != curvePhysics)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                                        Resources.EfficiencyTabCurveVsMatrix,
                                        curvePhysics, matrixPhysics));
            }

            return notes;
        }

        void efficiencyCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            this.UpdateEfficiencyView();
            if (!this.contentsLoading)
            {
                this.SetActiveDeviceConfigDirty();
            }
        }

        // ------------------------------------------------------------------
        // Кнопки списка
        // ------------------------------------------------------------------

        /// <summary>
        /// Создать конфигурацию: имя спрашивается здесь, содержимое — в
        /// конструкторе кривой. Пустая конфигурация заводится сразу, до того
        /// как в ней что-нибудь появится: конструктору нужно, во что писать.
        /// </summary>
        void efficiencyNewButton_Click(object sender, EventArgs e)
        {
            if (this.activeDeviceConfig == null)
            {
                return;
            }

            string name = AskName(this, Resources.EfficiencyTabRenameTitle,
                                  Resources.EfficiencyTabNewName);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            // Кривую в конструкторе даёт только расчёт из геометрии
            // (`AMBER25`, 13.09.2026): прежнее `Measurement` здесь было
            // заделом под фит по спектрам, которого больше нет.
            EfficiencyConfigData config = new EfficiencyConfigData(name)
            {
                Origin = EfficiencyOrigin.Simulation,
            };

            this.activeDeviceConfig.EfficiencyConfigs.Add(config);
            this.RefreshEfficiencyList(config.Guid);
            this.SetActiveDeviceConfigDirty();
            this.OpenEfficiencyMaker(config);
        }

        /// <summary>
        /// Изменить выбранную. По существу это правка её геометрии — и правка
        /// в том числе ОТСУТСТВУЮЩЕЙ: у кривой без геометрии конструктор
        /// открывается на заготовке, чтобы её было где дописать.
        /// </summary>
        void efficiencyEditButton_Click(object sender, EventArgs e)
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            if (config == null)
            {
                return;
            }

            this.OpenEfficiencyMaker(config);
        }

        // Открытые конструкторы кривой. Форма прибора правит КЛОН конфигурации,
        // и при смене строки списка (или отказе от сохранения) клон выбрасывается —
        // привязанный к нему конструктор писал бы своё «Сохранить» в объект,
        // до которого больше никому нет дела. Такие конструкторы закрываются
        // вместе с клоном (см. CloseEfficiencyMakers).
        readonly List<EfficiencyMakerForm> openEfficiencyMakers = new List<EfficiencyMakerForm>();

        /// <summary>
        /// Закрыть конструкторы, привязанные к выбрасываемому клону конфигурации.
        /// Молчаливая альтернатива хуже: окно оставалось бы живым, а его
        /// «Сохранить» уходило бы в сироту — часы монте-карло пропадали бы без
        /// единого признака.
        /// </summary>
        void CloseEfficiencyMakers()
        {
            foreach (EfficiencyMakerForm maker in this.openEfficiencyMakers.ToArray())
            {
                // (`AMBER201`) Без вопроса о несохранённом: здесь решение
                // принято вопросом о сохранении конфигурации прибора.
                maker.CloseWithoutAsking();
            }

            this.openEfficiencyMakers.Clear();
        }

        /// <summary>
        /// ⛔ (`AMBER184`, 05.10.2026; решение Amber вопросником, дословно: «Не
        /// закрывать (Рекомендую)») Клон конфигурации заменяется СВЕЖИМ клоном ТОЙ
        /// ЖЕ конфигурации (ответ «Нет» на вопрос о сохранении, освежение после
        /// записи мимо формы) — конструкторы кривой не закрываются, а
        /// перепривязываются к той же кривой в новом клоне
        /// (<see cref="EfficiencyMakerForm.Rebind"/>): счёт, поля геометрии и
        /// посчитанная кривая живут.
        ///
        ///   * конструктор ДРУГОЙ конфигурации (по Guid прибора) — закрывается,
        ///     как при смене строки списка;
        ///   * кривой в новом клоне нет (заведена «New...»/копией/импортом после
        ///     последнего сохранения и отброшена вместе с прочими правками):
        ///     терять в окне нечего — оно закрывается молча; есть что — вопрос
        ///     (<c>EfficiencyMakerKeepUnsavedCurve</c>): «Да» возвращает кривую в
        ///     новый клон и оставляет окно, «Нет» — отбрасывает и закрывает.
        /// </summary>
        /// <returns>Guid кривых, возвращённых в клон (клон после этого изменён,
        /// временные матрицы этих кривых снимать нельзя).</returns>
        List<string> RebindEfficiencyMakers(DeviceConfigInfo fresh)
        {
            List<string> restored = new List<string>();
            foreach (EfficiencyMakerForm maker in this.openEfficiencyMakers.ToArray())
            {
                if (maker.IsDisposed)
                {
                    this.openEfficiencyMakers.Remove(maker);
                    continue;
                }

                DeviceConfigInfo device = maker.BoundDevice;
                EfficiencyConfigData curve = maker.BoundConfig;
                if (fresh == null || device == null || curve == null || device.Guid != fresh.Guid)
                {
                    this.openEfficiencyMakers.Remove(maker);
                    maker.CloseWithoutAsking();
                    continue;
                }

                EfficiencyConfigData target = null;
                if (fresh.EfficiencyConfigs != null)
                {
                    foreach (EfficiencyConfigData item in fresh.EfficiencyConfigs)
                    {
                        if (item.Guid == curve.Guid)
                        {
                            target = item;
                            break;
                        }
                    }
                }

                if (target == null)
                {
                    bool keep = false;
                    if (maker.HasUnsavedWork && !restored.Contains(curve.Guid))
                    {
                        string text = string.Format(CultureInfo.CurrentCulture,
                            ResourceText("EfficiencyMakerKeepUnsavedCurve",
                                "The curve \"{0}\" is not in the saved device configuration: it was added after the last save"
                                + " and is discarded with the other changes." + Environment.NewLine + Environment.NewLine
                                + "Its curve designer holds unsaved work (geometry, a computed curve or a running calculation)."
                                + Environment.NewLine + Environment.NewLine
                                + "Yes - keep the curve in the configuration (the designer stays open, the configuration stays modified)."
                                + Environment.NewLine + "No - discard the curve and close the designer."),
                            curve.Name);
                        keep = MessageBox.Show(this, text, Resources.ConfirmationDialogTitle,
                                               MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.Yes;
                    }
                    else if (restored.Contains(curve.Guid))
                    {
                        keep = true;    // второй конструктор той же кривой
                    }

                    if (!keep)
                    {
                        this.openEfficiencyMakers.Remove(maker);
                        maker.CloseWithoutAsking();
                        continue;
                    }

                    if (!restored.Contains(curve.Guid))
                    {
                        fresh.EfficiencyConfigs.Add(curve);
                        restored.Add(curve.Guid);
                    }

                    target = curve;
                }

                maker.Rebind(fresh, target);
            }

            return restored;
        }

        /// <summary>
        /// Строка общих ресурсов по имени с запасным английским текстом — без
        /// правки <c>Resources.Designer.cs</c> (`AMBER184`).
        /// </summary>
        static string ResourceText(string key, string fallback)
        {
            try
            {
                string value = Resources.ResourceManager.GetString(key, Resources.Culture);
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        /// <summary>
        /// Открыть конструктор кривой для этой конфигурации. Окно немодальное —
        /// прогон по пачке спектров долгий, и держать за него конфигурацию
        /// прибора нельзя; правит оно тот же объект, что лежит в списке.
        /// </summary>
        void OpenEfficiencyMaker(EfficiencyConfigData config)
        {
            // (`AMBER201`, Р4, подозрение полосы 8) Второй конструктор на ТУ ЖЕ
            // кривую не заводится — поднимается открытый. Прежде «Изменить»
            // дважды давало два окна на один объект кривой: «Сохранить» одного
            // молча затирало сохранённое другим, и какое из двух легло в
            // конфигурацию, решал порядок нажатий.
            foreach (EfficiencyMakerForm open in this.openEfficiencyMakers)
            {
                if (!open.IsDisposed && object.ReferenceEquals(open.BoundConfig, config))
                {
                    if (open.WindowState == FormWindowState.Minimized)
                    {
                        open.WindowState = FormWindowState.Normal;
                    }

                    open.Activate();
                    return;
                }
            }

            EfficiencyMakerForm maker = new EfficiencyMakerForm();
            maker.BindTo(this.activeDeviceConfig, config);

            // Обновляться надо по ЗАКРЫТИЮ конструктора, а не сразу после Show:
            // окно немодальное, и сохранение случится когда-то потом. Без этой
            // подписки вкладка сразу после «Сохранить» показывала «кривая без
            // геометрии», держала «Изменить» недоступной и рисовала пустой
            // эскиз — при том что и кривая, и геометрия уже лежали в
            // конфигурации. Обманывал только вид, и заметить это можно было
            // единственным способом: переключить список туда и обратно.
            // (`AMBER184`) Привязка читается у самого окна, а не из захваченной
            // переменной: ответ «Нет» перепривязывает окно к свежему клону
            // (<see cref="RebindEfficiencyMakers"/>), и захваченный прежний клон
            // делал бы такое окно навсегда «чужим».
            maker.FormClosed += delegate
            {
                if (this.IsDisposed)
                {
                    return;
                }

                this.openEfficiencyMakers.Remove(maker);

                // Клон, к которому был привязан конструктор, уже заменён
                // (смена строки списка): обновлять вкладку не по чему, а
                // дирти-флажок относился бы к ЧУЖОЙ конфигурации.
                if (!object.ReferenceEquals(maker.BoundDevice, this.activeDeviceConfig))
                {
                    return;
                }

                this.RefreshEfficiencyList(maker.BoundConfig == null ? config.Guid : maker.BoundConfig.Guid);
                this.SetActiveDeviceConfigDirty();
            };

            // (`AMBER184`) «Сохранить» конструктора меняет клон конфигурации —
            // клон изменён СРАЗУ, а не с закрытием окна: иначе внешняя запись в
            // менеджер (панель калибровки) при «чистой» форме заменяла бы клон
            // свежим, и сохранённая в конструкторе кривая уходила бы с прежним.
            maker.SavedIntoConfig += delegate
            {
                if (this.IsDisposed || !object.ReferenceEquals(maker.BoundDevice, this.activeDeviceConfig))
                {
                    return;
                }

                // Выбор действующей кривой не трогаем — освежаем сводку.
                EfficiencyConfigData selectedNow = this.SelectedEfficiency();
                this.RefreshEfficiencyList(selectedNow == null ? null : selectedNow.Guid);
                this.SetActiveDeviceConfigDirty();
            };

            this.openEfficiencyMakers.Add(maker);
            maker.Show(this);
        }

        /// <summary>
        /// Завести кривую эффективности из текстового экспорта ЛСРМ
        /// (`EffCalcMC.txt`) и ПОЛОЖИТЬ ЕЁ В КОНФИГУРАЦИЮ ПРИБОРА.
        ///
        /// ⛔ `AMBER13`, решение Amber 10.09.2026: «`buttonLoadEff` /
        /// `labelEffNote` уходят с вкладки `DoseRate`, а ввоз экспорта ЛСРМ
        /// заводится на вкладке Efficiency, и ввезённая кривая наконец
        /// СОХРАНЯЕТСЯ». Прежний импорт клал точки в поле формы
        /// (`doseRateFileCurve`), откуда их нельзя было ни сохранить, ни
        /// посмотреть после закрытия окна.
        ///
        /// ⚠ Метод отделён от кнопки нарочно и окна не поднимает: разбор файла
        /// и попадание кривой в конфигурацию проверяются пробой без диалога.
        /// Геометрии у импортированной кривой НЕТ — экспорт ЛСРМ её не несёт, — и
        /// это законное состояние: <see cref="EfficiencyConfigData"/> без
        /// геометрии пользуется, но не пересчитывается. Кривую С геометрией
        /// заводит <see cref="ImportLsrmEfficiencyWithGeometry"/>; этот вход
        /// оставлен ровно в прежней подписи — его зовёт отражением проба П21
        /// (`DoseRateCleanupP21Probe`) по имени без списка параметров, и
        /// перегрузка того же имени сломала бы ей `GetMethod`.
        /// </summary>
        /// <returns>Заведённая конфигурация или null, если файл негоден.</returns>
        internal static EfficiencyConfigData ImportLsrmEfficiency(
            DeviceConfigInfo device, string path, out string problem)
        {
            string geometryProblem;
            return ImportLsrmEfficiencyWithGeometry(device, path, null, out problem, out geometryProblem);
        }

        /// <summary>
        /// Импорт экспорта ЛСРМ ВМЕСТЕ С ГЕОМЕТРИЕЙ — `AMBER18`, решение Amber
        /// 12.09.2026, вопросником, дословно: «Привязать геометрию при ввозе
        /// ЛСРМ». С 12.09.2026 мощность дозы считается от кривой панели, и у
        /// кривой БЕЗ геометрии дозы нет вовсе («у кривой «X» нет геометрии —
        /// у дозы нет масштаба»): между «квант/с в 4π» и «квант/(см²·с) в
        /// точке» стоит геометрия (`DoseRateGeometry.FluencePerPhoton`).
        /// Единственный импорт, дающий кривую без геометрии, — этот; ЛСРМ же
        /// считает свою кривую из файла `.in` той же геометрии, и файл у
        /// человека есть — его и спрашиваем вторым шагом.
        ///
        /// Правила:
        ///  * <paramref name="geometryPath"/> == null — отказ от `.in` (Cancel):
        ///    кривая импортируется как раньше, без геометрии, доза откажет словами;
        ///  * `.in` негоден (нет файла, не разбирается, нет кристалла, сцена
        ///    поля) — кривая ВСЁ РАВНО импортируется, без геометрии, а причина
        ///    уходит в <paramref name="geometryProblem"/>: точки кривой от
        ///    негодного `.in` не портятся, и терять их ради него незачем;
        ///  * геометрия читается ТЕМ ЖЕ читателем, что конструктор кривой
        ///    (<see cref="GeometryModel.Load"/>), — другого разбора `.in` в
        ///    дереве нет и не должно быть;
        ///  * `Origin` остаётся <see cref="EfficiencyOrigin.Lsrm"/>, а
        ///    <see cref="EfficiencyConfigData.ComputeStamp"/> — ПУСТЫМ: клеймо
        ///    значит «чем посчитана», а эту кривую считал ЛСРМ, не мы. Кривая с
        ///    геометрией и пустым клеймом — не «посчитанная из геометрии»:
        ///    матрицы у неё нет по построению, доза пойдёт со знаком «≈» (по
        ///    пиковой), как велит решение (3) `AMBER18`. Откуда кривая и
        ///    геометрия — в <see cref="EfficiencyConfigData.Note"/>.
        /// </summary>
        /// <param name="geometryPath">файл `.in` той же геометрии; null — без геометрии</param>
        /// <param name="problem">почему кривая НЕ импортирована (null — импортирована)</param>
        /// <param name="geometryProblem">почему геометрия НЕ привязана (null — привязана либо не просили)</param>
        /// <returns>Заведённая конфигурация или null, если экспорт негоден.</returns>
        public static EfficiencyConfigData ImportLsrmEfficiencyWithGeometry(
            DeviceConfigInfo device, string path, string geometryPath,
            out string problem, out string geometryProblem)
        {
            problem = null;
            geometryProblem = null;
            if (device == null)
            {
                // Не подпись для человека, а страж вызова: с кнопки сюда с
                // пустой конфигурацией не приходят, а проба обязана получить
                // причину. Поэтому строка не переводится и в resx не заводится.
                problem = "no device configuration selected";
                return null;
            }

            List<ROIEfficiencyData> points = ReadLsrmEfficiencyExport(path, out problem);
            if (problem != null)
            {
                return null;
            }

            EfficiencyConfigData config = new EfficiencyConfigData(
                Path.GetFileNameWithoutExtension(path))
            {
                Origin = EfficiencyOrigin.Lsrm,
                Curve = points,
            };

            string note = string.Format(CultureInfo.InvariantCulture, "LSRM export: {0}",
                                        Path.GetFileName(path));
            if (geometryPath != null)
            {
                GeometryModel geometry = ReadLsrmGeometry(geometryPath, out geometryProblem);
                if (geometry != null)
                {
                    config.Geometry = geometry;
                    // (`AMBER185`) Кривая ЛСРМ посчитана для этой геометрии —
                    // её отпечаток и ставится; правка геометрии потом видна.
                    config.GeometryFingerprint = ResponseMatrix.GeometryFingerprint(config.Geometry);
                    note += string.Format(CultureInfo.InvariantCulture, "; geometry: {0}",
                                          Path.GetFileName(geometryPath));
                    foreach (string warning in geometry.Warnings)
                    {
                        note += Environment.NewLine + warning;
                    }
                }
            }

            config.Note = new CDATA(note);
            device.EfficiencyConfigs.Add(config);
            return config;
        }

        /// <summary>
        /// Прочитать `.in` для кривой ЛСРМ и ПРОВЕРИТЬ, что это геометрия, с
        /// которой у дозы будет масштаб. null — негоден, причина в
        /// <paramref name="problem"/>.
        ///
        /// ⚠ Проверка нужна потому, что читатель `.in` — разбор `ключ = значение`
        /// и на чужом тексте НЕ падает: любой файл даёт модель с нулевым
        /// кристаллом, и дефект всплыл бы не здесь, а в дозе («кристалл нулевой
        /// глубины») — у человека, который никакого кристалла не трогал.
        /// Коаксиальный `.in` ЛСРМ (`DC_*`) читателем не берётся — германий вне
        /// работы, — и он же ловится здесь как «нет кристалла».
        /// </summary>
        public static GeometryModel ReadLsrmGeometry(string geometryPath, out string problem)
        {
            problem = null;
            GeometryModel geometry;
            try
            {
                geometry = GeometryModel.Load(geometryPath);
            }
            catch (Exception ex)
            {
                problem = string.Format(CultureInfo.InvariantCulture, "{0}: {1}", geometryPath, ex.Message);
                return null;
            }

            bool crystal = geometry.Shape == CrystalShape.Box
                ? geometry.CrystalBoxX > 0.0 && geometry.CrystalBoxY > 0.0 && geometry.CrystalBoxZ > 0.0
                : geometry.CrystalDiameter > 0.0 && geometry.CrystalHeight > 0.0;
            if (geometry.Raw.Count == 0 || !crystal)
            {
                problem = string.Format(CultureInfo.InvariantCulture,
                    FormText("lsrmGeometryNoCrystal",
                        "{0}: no scintillator crystal dimensions found (DS_CrystalDiameter / DS_CrystalHeight"
                        + " or DS_CrystalBoxX / Y / Z) - not an LSRM scintillator geometry."),
                    geometryPath);
                return null;
            }

            // Сцена поля — наше расширение, ЛСРМ его не знает; отклик там на
            // единичный флюенс (см²), а кривая ЛСРМ — доли квантов источника.
            // Пустить такую пару — значит получить в дозе отказ «пересчитайте
            // кривую из геометрии», который для кривой ЛСРМ невыполним.
            if (ResponseMatrix.NormalizationOf(geometry) == ResponseMatrixNormalization.PerUnitFluence)
            {
                problem = string.Format(CultureInfo.InvariantCulture,
                    FormText("lsrmGeometryFieldScene",
                        "{0}: a field scene (DS_Scene = ISO) cannot be the geometry of an LSRM curve"
                        + " - the LSRM efficiency is per emitted quantum, not per unit fluence."),
                    geometryPath);
                return null;
            }

            return geometry;
        }

        /// <summary>
        /// Автоподбор `.in`: одноимённый файл рядом с экспортом
        /// (`X.txt` → `X.in`). null — такого нет; тогда диалог открывается
        /// в каталоге экспорта пустым, и человек выбирает сам.
        /// </summary>
        public static string SuggestLsrmGeometryPath(string exportPath)
        {
            try
            {
                string candidate = Path.ChangeExtension(exportPath, ".in");
                return File.Exists(candidate) ? candidate : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Строка из ресурсов САМОЙ ФОРМЫ (пара `DeviceConfigForm.resx` /
        /// `DeviceConfigForm.ru.resx`, как у <c>crystalMaterialNotSet</c>) с
        /// запасным английским текстом: подписи импорта нужны этой вкладке и
        /// нигде больше, а общие `Properties/Resources` правят другие полосы.
        /// </summary>
        static string FormText(string key, string fallback)
        {
            try
            {
                string value = new System.ComponentModel.ComponentResourceManager(typeof(DeviceConfigForm))
                    .GetString(key);
                return string.IsNullOrEmpty(value) ? fallback : value;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        void efficiencyImportButton_Click(object sender, EventArgs e)
        {
            if (this.activeDeviceConfig == null)
            {
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = Resources.EfficiencyTabImportLsrmTitle;
            openFileDialog.Filter = Resources.EffCalcMCFileFilter;
            openFileDialog.FilterIndex = 2;
            openFileDialog.RestoreDirectory = true;
            if (openFileDialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            // Второй шаг (`AMBER18`, 12.09.2026): `.in` той же геометрии.
            // Cancel — законный ответ: кривая импортируется без геометрии, как
            // раньше, и доза по ней откажет словами, пока геометрию не зададут
            // кнопкой «Изменить…».
            string geometryPath = null;
            using (OpenFileDialog geometryDialog = new OpenFileDialog())
            {
                geometryDialog.Title = FormText("lsrmGeometryDialogTitle",
                    "Geometry of the LSRM curve: the .in file it was computed for (Cancel - import without geometry)");
                geometryDialog.Filter = Resources.EfficiencyMakerGeometryFilter;
                geometryDialog.RestoreDirectory = true;
                string suggested = SuggestLsrmGeometryPath(openFileDialog.FileName);
                if (suggested != null)
                {
                    geometryDialog.InitialDirectory = Path.GetDirectoryName(suggested);
                    geometryDialog.FileName = Path.GetFileName(suggested);
                }
                else
                {
                    geometryDialog.InitialDirectory = Path.GetDirectoryName(openFileDialog.FileName);
                }

                if (geometryDialog.ShowDialog(this) == DialogResult.OK)
                {
                    geometryPath = geometryDialog.FileName;
                }
            }

            string problem, geometryProblem;
            EfficiencyConfigData config = ImportLsrmEfficiencyWithGeometry(
                this.activeDeviceConfig, openFileDialog.FileName, geometryPath,
                out problem, out geometryProblem);
            if (config == null)
            {
                MessageBox.Show(this,
                    string.Format(Resources.ERRFileOpenFailure, openFileDialog.FileName, problem),
                    this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (geometryProblem != null)
            {
                // Кривая уже в конфигурации — сказать надо про геометрию, а не
                // про импорт: иначе человек решит, что импорт не удался, и повторит.
                MessageBox.Show(this,
                    string.Format(CultureInfo.CurrentCulture,
                        FormText("lsrmGeometryProblem",
                            "The geometry could not be attached to the curve \"{0}\":"
                            + Environment.NewLine + "{1}" + Environment.NewLine + Environment.NewLine
                            + "The curve is imported without geometry; the dose rate has no scale"
                            + " until a geometry is added with Edit..."),
                        config.Name, geometryProblem),
                    this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            this.RefreshEfficiencyList(config.Guid);
            this.SetActiveDeviceConfigDirty();
        }

        void efficiencyRenameButton_Click(object sender, EventArgs e)
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            if (config == null)
            {
                return;
            }

            string name = AskName(this, Resources.EfficiencyTabRenameTitle, config.Name);
            if (string.IsNullOrEmpty(name) || name == config.Name)
            {
                return;
            }

            config.Name = name;
            config.LastUpdated = DateTime.Now;
            this.RefreshEfficiencyList(config.Guid);
            this.SetActiveDeviceConfigDirty();
        }

        void efficiencyDuplicateButton_Click(object sender, EventArgs e)
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            if (config == null || this.activeDeviceConfig == null)
            {
                return;
            }

            EfficiencyConfigData copy = config.Duplicate(
                string.Format(Resources.EfficiencyTabCopySuffix, config.Name));
            this.activeDeviceConfig.EfficiencyConfigs.Add(copy);
            this.RefreshEfficiencyList(copy.Guid);
            this.SetActiveDeviceConfigDirty();
        }

        void efficiencyDeleteButton_Click(object sender, EventArgs e)
        {
            EfficiencyConfigData config = this.SelectedEfficiency();
            if (config == null || this.activeDeviceConfig == null)
            {
                return;
            }

            DialogResult answer = MessageBox.Show(this,
                string.Format(Resources.EfficiencyTabDeleteConfirm, config.Name),
                Resources.ConfirmationDialogTitle,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation);
            if (answer != DialogResult.OK)
            {
                return;
            }

            this.activeDeviceConfig.EfficiencyConfigs.Remove(config);
            // (`AMBER201`, Р4, мелочь 8.10) Конструктор, открытый на удалённой
            // кривой, закрывается вместе с ней. Прежде он оставался жив, и его
            // «Сохранить» клало геометрию и кривую в объект, которого в
            // конфигурации больше нет, — без единого признака; удаление кривой
            // человек только что подтвердил.
            foreach (EfficiencyMakerForm maker in this.openEfficiencyMakers.ToArray())
            {
                if (object.ReferenceEquals(maker.BoundConfig, config))
                {
                    this.openEfficiencyMakers.Remove(maker);
                    maker.CloseWithoutAsking();
                }
            }

            if (this.activeDeviceConfig.ActiveEfficiencyGuid == config.Guid)
            {
                this.activeDeviceConfig.ActiveEfficiencyGuid = null;
            }

            this.RefreshEfficiencyList(this.activeDeviceConfig.ActiveEfficiencyGuid);
            this.SetActiveDeviceConfigDirty();
        }

        void RefreshEfficiencyList(string selectGuid)
        {
            bool wasLoading = this.contentsLoading;
            this.contentsLoading = true;
            try
            {
                this.efficiencyCombo.Items.Clear();
                this.efficiencyCombo.Items.Add(Resources.EfficiencyTabNone);
                int selected = 0;
                if (this.activeDeviceConfig != null)
                {
                    foreach (EfficiencyConfigData item in this.activeDeviceConfig.EfficiencyConfigs)
                    {
                        int i = this.efficiencyCombo.Items.Add(item);
                        if (item.Guid == selectGuid)
                        {
                            selected = i;
                        }
                    }
                }

                this.efficiencyCombo.SelectedIndex = selected;
            }
            finally
            {
                this.contentsLoading = wasLoading;
            }

            this.UpdateEfficiencyView();
        }

        /// <summary>
        /// Однострочный ввод. Своё окошко, а не InputBox из VisualBasic: тянуть
        /// в проект целую сборку ради одного поля незачем, а её здесь нет.
        /// Пустая строка означает отказ.
        ///
        /// Открыто редактору геометрии (`AMBER24`): имя своего шаблона детектора
        /// спрашивается тем же окошком, а второго такого окошка заводить незачем.
        /// </summary>
        internal static string AskName(IWin32Window owner, string title, string current)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = title;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ClientSize = new Size(360, 96);
                dialog.Icon = Resources.becqmoni;

                TextBox box = new TextBox
                {
                    Location = new Point(12, 16),
                    Size = new Size(336, 20),
                    Text = current ?? "",
                };

                Button ok = new Button
                {
                    Location = new Point(180, 56),
                    Size = new Size(80, 26),
                    Text = Resources.GeometryEditorSave,
                    DialogResult = DialogResult.OK,
                };

                Button cancel = new Button
                {
                    Location = new Point(268, 56),
                    Size = new Size(80, 26),
                    Text = Resources.GeometryEditorCancel,
                    DialogResult = DialogResult.Cancel,
                };

                dialog.Controls.Add(box);
                dialog.Controls.Add(ok);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                box.SelectAll();

                return dialog.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : "";
            }
        }
    }
}
