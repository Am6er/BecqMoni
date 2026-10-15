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

        /// <summary>
        /// (П259, 14.10.2026) Высота шапки при сводке в ДВЕ строки — как её
        /// разложил конструктор вкладки. От неё растёт <see cref="efficiencyHeaderBaseHeight"/>,
        /// когда сводке нужно больше (<see cref="LayoutEfficiencySummary"/>).
        /// </summary>
        int efficiencyHeaderInitialHeight;

        /// <summary>Высота сводки в две строки — меньше она не становится.</summary>
        const int EfficiencySummaryMinHeight = 30;

        /// <summary>Просвет между сводкой и подписью о поколениях.</summary>
        const int EfficiencyGenerationGap = 4;

        /// <summary>
        /// (`AMBER218`, П244 07.10.2026) Guid кривых, у которых СТАРАЯ кривая
        /// или СТАРАЯ матрица отклика — те, что в списке рисуются с меткой
        /// «!» на красной подложке (<see cref="DrawStaleMark"/>). Решение Amber
        /// 07.10.2026 вопросником, дословно: кривая — **«Всё, о чём вкладка
        /// говорит «пересчитайте»»**, матрица — **«Всё, что окно матрицы зовёт
        /// «устарела»»**.
        ///
        /// Набор считается при ЗАПОЛНЕНИИ списка и после записи матрицы, а не
        /// при каждой перерисовке: признак матрицы читает её файл со склада
        /// (<see cref="MatrixStale"/>), и делать это на каждый
        /// <c>DrawItem</c> значило бы читать склад при каждом движении мыши по
        /// списку.
        /// </summary>
        HashSet<string> efficiencyStaleGuids = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Текст метки старой кривой или матрицы в списке.</summary>
        internal const string StaleMarkText = "!";

        /// <summary>Просвет между меткой и именем геометрии, точек.</summary>
        const int StaleMarkGap = 4;

        // ⛔ (`AMBER227`, П331 15.10.2026) Кнопки «Импорт ЛСРМ...» (`efficiencyImportButton`,
        // `AMBER13`) на вкладке БОЛЬШЕ НЕТ — снята вместе со своим путём: обработчиком,
        // `ImportLsrmEfficiency`/`ImportLsrmEfficiencyWithGeometry`, `ReadLsrmGeometry`,
        // `SuggestLsrmGeometryPath`, `FormText` и их строками ресурсов. Amber 15.10.2026,
        // дословно: «Эту кнопку удали и подтяни GUI в связи с удалением кнопки так как
        // появилось пустое место», и на вопросник — «Убрать эту логику». Кривая ЛСРМ
        // (txt, .efa, .efr) импортируется в конструкторе: «Импорт из LSRM…» редактора
        // геометрии (`AMBER226`), геометрия к ней — полями или `.in` той же кнопкой.

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
            // добавленной или снятой строке: панель обрезает детей МОЛЧА, без
            // исключения и без признака, а лишняя высота остаётся пустой полосой.
            // Третий ряд кнопок (импорт ЛСРМ, `AMBER13`) прибавлял 32 точки
            // (166 → 198); с его снятием (`AMBER227`) — снова 166.
            this.efficiencyHeader = new Panel { Dock = DockStyle.Top, Height = 166 };
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

            this.efficiencyNewButton.Click += this.efficiencyNewButton_Click;
            this.efficiencyEditButton.Click += this.efficiencyEditButton_Click;
            this.efficiencyRenameButton.Click += this.efficiencyRenameButton_Click;
            this.efficiencyDuplicateButton.Click += this.efficiencyDuplicateButton_Click;
            this.efficiencyDeleteButton.Click += this.efficiencyDeleteButton_Click;
            this.efficiencyMatrixButton.Click += this.efficiencyMatrixButton_Click;

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
                // (`AMBER218`) Строки рисуются своими руками: перед именем
                // геометрии со старой кривой или старой матрицей — метка «!»
                // белым на красном квадрате (<see cref="DrawStaleMark"/>).
                DrawMode = DrawMode.OwnerDrawFixed,
            };

            this.efficiencyCombo.DrawItem += this.efficiencyCombo_DrawItem;
            this.efficiencyCombo.SelectedIndexChanged += this.efficiencyCombo_SelectedIndexChanged;
            header.Controls.Add(this.efficiencyCombo);

            y += 28;
            this.efficiencySummaryLabel = new Label
            {
                AutoSize = false,
                Location = new Point(Margin, y),
                Size = new Size(Width, EfficiencySummaryMinHeight),
                ForeColor = Color.DimGray,
            };

            header.Controls.Add(this.efficiencySummaryLabel);

            // Подпись о расхождении поколений (`A119`) — под сводкой, своим
            // цветом и своей высотой. Заводится пустой и невидимой: у сцены, где
            // кривая, матрица и сборка одного поколения, сказать нечего, и
            // пустая строка не должна отнимать у чертежа ни точки.
            y += EfficiencySummaryMinHeight + EfficiencyGenerationGap;
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
            this.efficiencyHeaderInitialHeight = header.Height;

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
            this.efficiencyStaleGuids = StaleEfficiencyGuids(config);
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
                // (`AMBER186`) Записанная окном матрица лежит во ВРЕМЕННОМ
                // файле и уйдёт на склад только сохранением конфигурации —
                // значит, конфигурация изменена, и вопрос «сохранить?» обязан
                // прозвучать: «Да» перенесёт матрицу, «Нет» снимет её.
                if (form.MatrixSavedPending)
                {
                    this.SetActiveDeviceConfigDirty();
                }
            }

            this.UpdateEfficiencyView();
        }

        void responseMatrixForm_MatrixSaved(object sender, EventArgs e)
        {
            // (`AMBER218`) Записанная матрица могла снять метку «!» со своей
            // строки (или поставить — если посчитана для другой геометрии):
            // набор пересчитывается, список перерисовывается.
            this.efficiencyStaleGuids = StaleEfficiencyGuids(this.activeDeviceConfig);
            this.efficiencyCombo.Invalidate();
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
                this.LayoutEfficiencySummary();
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
            this.LayoutEfficiencySummary();

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
            List<string> notes = CurveSceneNotes(config);
            if (config != null && config.HasGeometry)
            {
                notes.AddRange(config.Geometry.Warnings);
            }

            return notes;
        }

        /// <summary>
        /// Та часть <see cref="GeometryNotes"/>, которая говорит о КРИВОЙ —
        /// «посчитана для другой геометрии» и «прежняя сцена сосуда»; без
        /// предупреждений о слоях самой геометрии. Вынесена отдельно
        /// (`AMBER218`): по ней же решается метка «!» в списке
        /// (<see cref="CurveStale"/>), а слой без вещества — беда геометрии,
        /// а не старость кривой.
        /// </summary>
        internal static List<string> CurveSceneNotes(EfficiencyConfigData config)
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

            // ⛔ (`AMBER205`, П235 05.10.2026) Сцена сосуда полной высоты. Матрица
            // такой геометрии пересчитывается сама (её клеймо несёт `bmscene=1`
            // и перестаёт сходиться), кривая — НЕТ: она лежит числами. Кривая
            // нынешней физики без куска у сосуда, чья сцена правда изменилась,
            // посчитана прежней сценой (стенка только на высоту пробы, без
            // дальнего донышка). Кривая без `phys=` (ручная, по измерениям) —
            // молчим, как и `GenerationNotes`.
            string stamp = config.ComputeStamp ?? "";
            if (ResponseMatrix.PhysicsFromStamp(stamp) > 0
                && EfficiencyCalculation.VesselStampExpected(config.Geometry)
                && stamp.IndexOf(EfficiencyCalculation.VesselStamp, StringComparison.Ordinal) < 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                                        Resources.EfficiencyTabCurveOldVessel,
                                        EfficiencyCalculation.VesselStamp));
            }

            return notes;
        }

        // ------------------------------------------------------------------
        // Метка «!» старой кривой или старой матрицы в списке (AMBER218)
        // ------------------------------------------------------------------

        /// <summary>
        /// ⛔ (`AMBER218`, П244 07.10.2026) СТАРАЯ ЛИ КРИВАЯ — РОВНО ТО, О ЧЁМ
        /// ПОДПИСЬ ПОД СПИСКОМ ГОВОРИТ «ПЕРЕСЧИТАЙТЕ». Решение Amber 07.10.2026
        /// вопросником, дословно: **«Всё, о чём вкладка говорит «пересчитайте»»**:
        ///
        ///   * поколение переноса кривой ниже поколения сборки
        ///     (<see cref="GenerationNotes"/>, `phys=`);
        ///   * прежнее определение пика — у геометрии с разрешением нет
        ///     `peps=fwhm` (там же);
        ///   * посчитана для другой геометрии — отпечаток при кривой не
        ///     сходится с геометрией рядом (<see cref="CurveSceneNotes"/>);
        ///   * прежняя сцена сосуда — нет `bmscene=1` у сосуда, чья сцена
        ///     изменилась (там же).
        ///
        /// Своего правила здесь НЕТ нарочно: метка в списке и подпись под ним
        /// обязаны говорить одно, и двум копиям правила разойтись нельзя.
        /// Расхождение поколений кривой и матрицы между собой сюда не входит:
        /// с матрицей разбирается <see cref="MatrixStale"/>, и у пары «кривая 22,
        /// матрица 26» метку ставит кривая, у «кривая 26, матрица 22» — матрица.
        /// Кривая без клейма (ручная, по измерениям) — не старая: сказать о ней
        /// нечего, как и подписи.
        /// </summary>
        internal static bool CurveStale(EfficiencyConfigData config)
        {
            if (config == null)
            {
                return false;
            }

            return GenerationNotes(config.ComputeStamp, 0, ResponseMatrix.PhysicsVersion,
                                   EfficiencyCalculation.PeakWindowExpected(config.Geometry)).Count > 0
                   || CurveSceneNotes(config).Count > 0;
        }

        /// <summary>
        /// ⛔ (`AMBER218`) СТАРАЯ ЛИ МАТРИЦА — РОВНО ТО, ЧТО ОКНО «Response matrix»
        /// ЗОВЁТ «УСТАРЕЛА». Решение Amber 07.10.2026 вопросником, дословно:
        /// **«Всё, что окно матрицы зовёт «устарела»»** — три состояния
        /// <c>ResponseMatrixForm.LoadExisting</c> / <c>SayRefusal</c>:
        ///
        ///   * файл прежнего формата (<see cref="MatrixRefusal.OldFormat"/>);
        ///   * поколение переноса в клейме не равно поколению сборки
        ///     (<see cref="ResponseMatrix.PhysicsVersion"/>);
        ///   * клеймо не сходится с нынешней геометрией и своими же параметрами
        ///     (<see cref="ResponseMatrix.IsValidFor(GeometryModel)"/>) —
        ///     геометрию правили после счёта.
        ///
        /// НЕ старая: матрицы нет вовсе (её не считали), файл обрублен, файл не
        /// наш — окно говорит об этом другими словами, и «старой версией» это
        /// не является. Кривая без геометрии матрицы иметь не может.
        ///
        /// Читается ТО ЖЕ место, что показывает окно, — ждущая сохранения
        /// матрица новее склада (<see cref="ResponseMatrixStore.EditingSource"/>,
        /// `AMBER186`). Файл читается целиком тем же <see cref="ResponseMatrixStore.Load(string, ResponseMatrixSource, out MatrixRefusal, out int)"/>,
        /// что и окно: годность по геометрии требует параметров матрицы, а их
        /// хвосты лежат ЗА телом файла, и заголовком не обойтись.
        /// </summary>
        internal static bool MatrixStale(EfficiencyConfigData config)
        {
            if (config == null || !config.HasGeometry || string.IsNullOrEmpty(config.Guid))
            {
                return false;
            }

            MatrixRefusal refusal;
            int fileFormat;
            ResponseMatrix matrix = ResponseMatrixStore.Load(config.Guid,
                                                             ResponseMatrixStore.EditingSource(config.Guid),
                                                             out refusal, out fileFormat);
            if (matrix == null)
            {
                return refusal == MatrixRefusal.OldFormat;
            }

            return ResponseMatrix.PhysicsFromStamp(matrix.Stamp) != ResponseMatrix.PhysicsVersion
                   || !matrix.IsValidFor(config.Geometry);
        }

        /// <summary>
        /// Ставить ли метку «!» этой кривой в списке: старая кривая ИЛИ старая
        /// матрица — «не имеет значения, что именно» (постановка Amber
        /// 07.10.2026).
        /// </summary>
        internal static bool EfficiencyStale(EfficiencyConfigData config)
        {
            return CurveStale(config) || MatrixStale(config);
        }

        /// <summary>
        /// Guid кривых прибора, которым положена метка. Пустая конфигурация —
        /// пустой набор.
        /// </summary>
        internal static HashSet<string> StaleEfficiencyGuids(DeviceConfigInfo config)
        {
            HashSet<string> stale = new HashSet<string>(StringComparer.Ordinal);
            if (config == null || config.EfficiencyConfigs == null)
            {
                return stale;
            }

            foreach (EfficiencyConfigData item in config.EfficiencyConfigs)
            {
                if (item != null && !string.IsNullOrEmpty(item.Guid) && EfficiencyStale(item))
                {
                    stale.Add(item.Guid);
                }
            }

            return stale;
        }

        /// <summary>
        /// Нарисовать метку — белый «!» на красном квадрате — у левого края
        /// <paramref name="bounds"/>, высотой в строку. Возвращает ширину,
        /// занятую меткой вместе с просветом: с неё начинается имя геометрии.
        /// Так постановка Amber 07.10.2026, дословно: «белым восклицательным
        /// знаком в красной квадратной подложке (красный квадрат, а в нём белый
        /// воскл знак) перед текстом имя геометрии. Формат, например "! Цилиндр"».
        ///
        /// Статический и без формы нарочно: проба рисует его в картинку и
        /// считает красные и белые точки, а не снимает список с экрана.
        /// </summary>
        internal static int DrawStaleMark(Graphics graphics, Rectangle bounds, Font font)
        {
            int side = Math.Max(4, bounds.Height - 2);
            Rectangle square = new Rectangle(bounds.X + 1, bounds.Y + (bounds.Height - side) / 2, side, side);
            using (SolidBrush red = new SolidBrush(Color.Red))
            {
                graphics.FillRectangle(red, square);
            }

            using (Font bold = new Font(font, FontStyle.Bold))
            {
                TextRenderer.DrawText(graphics, StaleMarkText, bold, square, Color.White,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                                      | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }

            return 1 + side + StaleMarkGap;
        }

        /// <summary>
        /// Строка списка: метка (если кривой положена) и имя. Рисуется и в
        /// раскрытом списке, и в закрытом поле (<see cref="DrawItemState.ComboBoxEdit"/>)
        /// — выбранная старая геометрия помечена и со свёрнутым списком.
        /// </summary>
        void efficiencyCombo_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            ComboBox combo = (ComboBox)sender;
            if (e.Index >= 0 && e.Index < combo.Items.Count)
            {
                object item = combo.Items[e.Index];
                EfficiencyConfigData config = item as EfficiencyConfigData;
                int left = e.Bounds.X;
                if (config != null && this.efficiencyStaleGuids.Contains(config.Guid ?? ""))
                {
                    left += DrawStaleMark(e.Graphics, e.Bounds, e.Font);
                }
                else
                {
                    left += 1;
                }

                Rectangle textBounds = new Rectangle(left, e.Bounds.Y, Math.Max(0, e.Bounds.Right - left), e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, item == null ? "" : item.ToString(), e.Font, textBounds,
                                      e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                                                   | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            e.DrawFocusRectangle();
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
        /// (П259, 14.10.2026) ⛔ ВЫСОТА СВОДКИ ТОЖЕ СЧИТАЕТСЯ, А НЕ ПИШЕТСЯ ЧИСЛОМ.
        /// Сводка жила в 466×30 — две строки, — а клеймо кривой с физики 18 и
        /// дальше переросло их: у кривой «Маринелли» прибора RadiaScan 701A
        /// на экране обрывалось на «elmix=1;», и третьей строки
        /// («lbrem=1; lbang=1; pairth=1; peps=fwhm; bmscene=1; mdb=…») не было
        /// видно вовсе — ровно тех ключей, отсутствие которых называет подпись
        /// о поколениях ниже (`peps=fwhm`, `bmscene=1`). Человек, сверяя слова
        /// подписи с клеймом, не нашёл бы их и при наличии. Теперь сводка
        /// растёт по тексту (не ниже двух строк), подпись о поколениях и шапка
        /// сдвигаются вместе с ней; чертёж доковый и подвинется сам.
        /// </summary>
        void LayoutEfficiencySummary()
        {
            Label summary = this.efficiencySummaryLabel;
            if (summary == null || this.efficiencyHeader == null)
            {
                return;
            }

            int need = Math.Max(EfficiencySummaryMinHeight,
                                GenerationLabelHeight(summary.Text, summary.Font, summary.Width));
            summary.Height = need;
            if (this.efficiencyGenerationLabel != null)
            {
                this.efficiencyGenerationLabel.Top = summary.Bottom + EfficiencyGenerationGap;
            }

            this.efficiencyHeaderBaseHeight = this.efficiencyHeaderInitialHeight
                                              + (need - EfficiencySummaryMinHeight);
            this.efficiencyHeader.Height = this.efficiencyHeaderBaseHeight;
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
                // (П259, 14.10.2026) Флажок изменённости здесь больше НЕ ставится.
                // С `AMBER184` его ставит «Сохранить» конструктора (`SavedIntoConfig`
                // ниже) — единственный путь, которым конструктор пишет в клон:
                // поля геометрии правят СВОЮ копию (`GeometryEditorPanel.SetModel`
                // клонирует). Прежний безусловный вызов объявлял конфигурацию
                // изменённой, когда окно открыли и закрыли крестиком, ничего не
                // сохранив: «Сохранить» прибора загоралось, а закрытие формы
                // спрашивало «Configuration has been modified. Save?» — замер
                // экраном 14.10.2026 на «Маринелли» RadiaScan 701A.
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
                this.efficiencyStaleGuids = StaleEfficiencyGuids(this.activeDeviceConfig);
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
