using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    /// <summary>
    /// Конструктор кривой эффективности регистрации.
    ///
    /// На вход — пачка спектров, снятых В ОДНОЙ геометрии, и, если она есть,
    /// прежняя кривая. На выход — ROI-файл с кривой, восстановленной из самих
    /// измерений: линии одной цепочки в вековом равновесии обязаны лечь на одну
    /// кривую, и по их разбросу кривая и строится (см. EfficiencyFitter).
    ///
    /// Кривая привязана к прибору И геометрии: эффективность полного
    /// поглощения зависит от телесного угла и самопоглощения в пробе. Пачка
    /// спектров разных геометрий даст бессмысленную среднюю кривую, и форма об
    /// этом предупреждает в заголовке списка.
    ///
    /// Второй путь — «Посчитать из геометрии»: кривая берётся не из измерений,
    /// а из файла геометрии `.in`, монте-карловским переносом
    /// (<see cref="EfficiencyCalculation"/>). Спектры для него не нужны вовсе, и
    /// уровень получается АБСОЛЮТНЫЙ, а не подогнанный: восстановление из
    /// равновесия даёт только форму. Оба пути кладут результат в одно и то же
    /// место, так что кривую можно посмотреть на графике и сохранить одинаково.
    /// </summary>
    public partial class EfficiencyMakerForm : Form
    {
        readonly List<string> spectrumFiles = new List<string>();
        /// <summary>
        /// Исходная кривая, по которой берётся АБСОЛЮТНЫЙ уровень подгонки, —
        /// кривая привязанной конфигурации прибора (см. <see cref="BindTo"/>).
        /// Её нет — уровень остаётся взять опорной точкой, иначе подгонка даёт
        /// одну форму, и так и написано в отчёте.
        /// </summary>
        List<ROIEfficiencyData> referenceCurve = null;
        EfficiencyFitResult lastResult;
        GeometryModel geometry;
        BackgroundWorker worker;
        volatile bool cancelRequested;

        /// <summary>
        /// Историй на точку кривой при расчёте из геометрии. Погрешность идёт
        /// как 1/√N: на 200 тысячах это около процента в середине шкалы и
        /// несколько процентов на её верху, где эффективность мала. Больше
        /// смысла имеет мало — систематика модели крупнее.
        /// </summary>
        const int SimulationHistories = 200000;

        public EfficiencyMakerForm()
        {
            InitializeComponent();
            BuildGeometryTab();
            // Считать есть по чему сразу: в редакторе всегда лежит геометрия —
            // либо заготовка, либо конфигурация, либо импортированный файл.
            // Прежде кнопку включал только импорт, и выбранный готовый детектор
            // посчитать было нельзя.
            this.calculateButton.Enabled = true;
            LoadChains();
            UpdateGraphMode();
            UpdateGeometryLayout();
            SetUpHints();
        }

        GeometryEditorPanel geometryPanel;

        TabPage geometryTabPage;

        /// <summary>
        /// Правили ли что-нибудь с последнего сохранения: геометрию руками или
        /// кривую пересчётом. Отражается звёздочкой в заголовке.
        /// </summary>
        bool dirty;

        /// <summary>
        /// Редактор геометрии — ПЕРВОЙ вкладкой: с геометрии начинается и
        /// расчёт, и импорт, а прежде она была строкой пути к чужому файлу.
        /// </summary>
        void BuildGeometryTab()
        {
            this.geometryPanel = new GeometryEditorPanel { Dock = DockStyle.Fill };
            this.geometryPanel.Changed += this.GeometryChanged;

            this.geometryTabPage = new TabPage(Resources.EfficiencyMakerTabGeometry)
            {
                UseVisualStyleBackColor = true,
                Padding = new Padding(3),
            };

            // Импорт чужой геометрии — здесь, а не строкой пути на вкладке
            // расчёта: файл `.in` не «выбирают на время», его СОДЕРЖИМОЕ
            // заезжает в поля и дальше живёт в конфигурации прибора. Обратной
            // записи в `.in` больше нет, поэтому и пути хранить незачем.
            Button import = new Button
            {
                Dock = DockStyle.Top,
                Height = 26,
                Text = Resources.EfficiencyTabImport,
                UseVisualStyleBackColor = true,
            };

            import.Click += this.ImportGeometryClick;

            Panel importRow = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(0, 4, 0, 4) };
            import.Dock = DockStyle.None;
            import.Width = 200;
            import.Location = new System.Drawing.Point(0, 4);
            importRow.Controls.Add(import);

            this.geometryTabPage.Controls.Add(this.geometryPanel);
            this.geometryTabPage.Controls.Add(importRow);

            // Не Insert: TabPages.Insert до создания дескриптора окна кладёт
            // страницу только в Controls, а на форме её нет — молча, без
            // исключения. Снимаем хвост и возвращаем следом.
            List<TabPage> tail = new List<TabPage>();
            while (this.tabControl.TabPages.Count > 0)
            {
                tail.Add(this.tabControl.TabPages[0]);
                this.tabControl.TabPages.RemoveAt(0);
            }

            this.tabControl.TabPages.Add(this.geometryTabPage);
            foreach (TabPage page in tail)
            {
                this.tabControl.TabPages.Add(page);
            }
        }

        /// <summary>
        /// Загрузить геометрию из файла LSRM `.in` прямо в поля редактора.
        /// Предупреждения разбора идут в журнал сразу: слой без вещества там не
        /// исчезает, а замещается соседним, и кривая выходит правдоподобной и
        /// чужой.
        /// </summary>
        void ImportGeometryClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = Resources.EfficiencyMakerGeometryFilter;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    GeometryModel model = GeometryModel.Load(dialog.FileName);
                    this.geometryPanel.SetModel(model);
                    this.geometry = model;
                    this.calculateButton.Enabled = true;
                    AppendLog(string.Format(Resources.EfficiencyMakerGeometryLoaded, model.Describe()));
                    foreach (string warning in model.Warnings)
                    {
                        AppendLog(warning);
                    }

                    this.SetDirty();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, this.Text,
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void GeometryChanged(object sender, EventArgs e)
        {
            this.SetDirty();
        }

        DeviceConfigInfo boundDevice;

        EfficiencyConfigData boundConfig;

        /// <summary>
        /// Привязать окно к конфигурации эффективности конкретного прибора.
        /// С этого момента «Сохранить» пишет В НЕЁ, а не в файл, и она же даёт
        /// исходную кривую — ту, с которой берётся абсолютный уровень подгонки.
        /// Раньше её выбирали ROI-файлом; выбирать больше нечего, кривая своя.
        ///
        /// Конфигурация правится НА МЕСТЕ, и это осознанно: список на вкладке
        /// прибора и это окно смотрят на один объект, поэтому «Сохранить»
        /// достаточно нажать здесь.
        /// </summary>
        public void BindTo(DeviceConfigInfo device, EfficiencyConfigData config)
        {
            this.boundDevice = device;
            this.boundConfig = config;
            if (config == null)
            {
                return;
            }

            if (config.Geometry != null)
            {
                this.geometryPanel.SetModel(config.Geometry);
                this.geometry = config.Geometry;
                this.calculateButton.Enabled = true;
            }

            // Кривая конфигурации становится исходной: по ней подгонка получает
            // уровень, и по ней же график рисует полосу отличий.
            this.referenceCurve = config.HasCurve ? config.Curve : null;
            this.graph.SetData(this.referenceCurve, this.lastResult);
            this.dirty = false;
            this.UpdateTitle();
            this.UpdateSaveState();
        }

        /// <summary>
        /// Положить посчитанное в привязанную конфигурацию. false — сохранять
        /// некуда либо геометрия в полях неверна, о чём уже сказано.
        /// </summary>
        bool SaveIntoConfig()
        {
            if (this.boundConfig == null)
            {
                return false;
            }

            // Геометрия забирается из полей, а не из this.geometry: правку в
            // полях, не нажав ничего больше, иначе потеряли бы молча.
            if (!this.geometryPanel.TryCommit())
            {
                return false;
            }

            this.boundConfig.Geometry = this.geometryPanel.Model;
            if (this.lastResult != null && this.lastResult.Ok)
            {
                List<ROIEfficiencyData> curve = new List<ROIEfficiencyData>();
                foreach (ROIEfficiencyData point in this.lastResult.Curve)
                {
                    curve.Add(point.Clone());
                }

                this.boundConfig.Curve = curve;
                this.boundConfig.Origin = this.lastResult.LevelSource == EfficiencyLevelSource.Simulation
                    ? EfficiencyOrigin.Simulation
                    : EfficiencyOrigin.Measurement;
            }

            this.boundConfig.LastUpdated = DateTime.Now;
            this.referenceCurve = this.boundConfig.HasCurve ? this.boundConfig.Curve : null;
            this.dirty = false;
            this.UpdateTitle();
            this.UpdateSaveState();
            return true;
        }

        /// <summary>
        /// На вкладке редактора геометрии всё, что ниже её, лишнее: график
        /// кривой и журнал прогона относятся к РАСЧЁТУ, а не к правке
        /// геометрии. Освободившуюся высоту
        /// забирает сама вкладка — в 282 точки её поля не помещались, и половина
        /// обвязки кристалла оказывалась за краем.
        /// </summary>
        void UpdateGeometryLayout()
        {
            // Кнопки сохранения, статус и полоса хода — ОБЩИЕ для всех вкладок
            // и стоят внизу формы, а не внутри вкладки: сохраняют они одно и то
            // же, с какой бы вкладки на них ни нажали.
            bool geometry = this.tabControl.SelectedTab == this.geometryTabPage;
            this.splitContainer.Visible = !geometry;

            // Высота у каждой вкладки СВОЯ, по её содержимому. Общая по самой
            // высокой оставляла на вкладке расчёта пустую полосу в полтораста
            // точек: там всего подсказка и одна кнопка.
            int height;
            if (geometry)
            {
                height = this.saveButton.Top - this.tabControl.Top - 12;
            }
            else if (this.tabControl.SelectedTab == this.tabPageCalculate)
            {
                height = CalculateTabHeight;
            }
            else
            {
                height = FitTabHeight;
            }

            this.tabControl.Height = height;

            // График с журналом идут следом, а не стоят на месте: иначе на
            // низкой вкладке между ними и вкладками зиял бы тот же провал.
            if (!geometry)
            {
                int top = this.tabControl.Bottom + 12;
                this.splitContainer.Top = top;
                this.splitContainer.Height = this.saveButton.Top - top - 12;
            }
        }

        /// <summary>Подсказка и одна кнопка — больше на вкладке расчёта нет.</summary>
        const int CalculateTabHeight = 116;

        /// <summary>Список спектров и рамка настроек — по самой высокой из них.</summary>
        const int FitTabHeight = 282;

        /// <summary>
        /// Когда «Сохранить» доступна. Правка геометрии сохраняется САМА ПО
        /// СЕБЕ, без пересчёта кривой: геометрию правят и сохраняют отдельно, а
        /// требовать ради этого прогона монте-карло значило бы отнимать минуты
        /// за чужой счёт.
        /// </summary>
        void UpdateSaveState()
        {
            bool haveCurve = this.lastResult != null && this.lastResult.Ok;
            this.saveButton.Enabled = this.boundConfig != null
                ? (this.dirty || haveCurve)
                : haveCurve;
            this.exportButton.Enabled = haveCurve;
        }

        /// <summary>
        /// Взвести признак правки и показать его в заголовке. Пока общей кнопки
        /// сохранения нет, звёздочка — единственный видимый признак того, что
        /// сделанное ещё никуда не легло.
        /// </summary>
        void SetDirty()
        {
            if (this.dirty)
            {
                return;
            }

            this.dirty = true;
            this.UpdateTitle();
            this.UpdateSaveState();
        }

        void UpdateTitle()
        {
            string title = this.boundConfig == null
                ? Resources.EfficiencyMakerTitle
                : string.Format("{0} - {1}",
                                this.boundDevice == null ? "" : this.boundDevice.Name,
                                this.boundConfig.Name);
            if (this.dirty)
            {
                title = Resources.EfficiencyMakerDirtyMark + title;
            }

            this.Text = title;
        }

        /// <summary>
        /// Подсказки к настройкам. Пишутся для того, кто спектрометрией не
        /// занимается: что это число делает с кривой и что будет, если его
        /// подвинуть, — без слов «полином», «сигма» и «квантовый выход» там,
        /// где без них можно обойтись.
        ///
        /// Подсказка висит и на подписи, и на самом поле: мышь ведут к тому,
        /// что читают, а читают подпись.
        /// </summary>
        void SetUpHints()
        {
            // Тексты длинные, и пяти секунд по умолчанию на них не хватает:
            // подсказка гаснет на середине фразы.
            this.hints.AutoPopDelay = 30000;
            this.hints.InitialDelay = 400;
            this.hints.ReshowDelay = 100;

            Action<Control, Control, string> hint = (label, field, text) =>
            {
                if (label != null)
                {
                    this.hints.SetToolTip(label, text);
                }

                if (field != null)
                {
                    this.hints.SetToolTip(field, text);
                }
            };

            hint(this.orderLabel, this.orderNumericUpDown, Resources.EfficiencyMakerTipOrder);
            hint(this.minIntensityLabel, this.minIntensityNumericUpDown,
                 Resources.EfficiencyMakerTipMinIntensity);
            hint(this.minSignificanceLabel, this.minSignificanceNumericUpDown,
                 Resources.EfficiencyMakerTipMinSignificance);
            hint(this.anchorLabel, this.anchorEnergyTextBox, Resources.EfficiencyMakerTipAnchorEnergy);
            hint(null, this.anchorEfficiencyTextBox, Resources.EfficiencyMakerTipAnchorEfficiency);
        }

        /// <summary>
        /// Отличия от исходной кривой показываются только на вкладке подгонки.
        /// Расчёт из геометрии не поправляет прежнюю кривую, а даёт свою с
        /// абсолютным уровнем; показывать его расхождение с чужой кривой как
        /// «отличие» — выдавать за поправку то, что поправкой не является.
        /// </summary>
        void UpdateGraphMode()
        {
            this.graph.ShowDifference = this.tabControl.SelectedTab == this.tabPageFit;
        }

        void tabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateGraphMode();
            UpdateGeometryLayout();
        }

        /// <summary>Наборы нуклидов, доступные в выпадающем списке строки.</summary>
        readonly List<string> chainNames = new List<string>();

        void LoadChains()
        {
            this.spectrumColumn.HeaderText = Resources.EfficiencyMakerColumnSpectrum;
            this.nuclideSetColumn.HeaderText = Resources.EfficiencyMakerColumnNuclideSet;
            this.ReloadNuclideSets(true);
        }

        /// <summary>
        /// Перечитать наборы нуклидов из конфига и обновить выпадающий список.
        ///
        /// Список строился один раз при открытии формы, а наборы заводят в
        /// соседнем окне, не закрывая эту: только что созданный набор в списке
        /// не появлялся. Теперь список перечитывается ещё и в тот момент, когда
        /// его раскрывают, — это единственный момент, когда он кому-то нужен.
        ///
        /// Наборы, которые в кривую не годятся, НАЗЫВАЮТСЯ в журнале с причиной.
        /// Молчаливое исчезновение неотличимо от «программа не видит мой набор»,
        /// а причина всегда чинится руками: дописать выходы, добавить вторую
        /// линию, развести одинаковые имена.
        /// </summary>
        /// <param name="verbose">Писать ли причины в журнал.</param>
        void ReloadNuclideSets(bool verbose)
        {
            List<EfficiencyLibrary.SetReject> rejected;
            Dictionary<string, List<EfficiencyLine>> chains = EfficiencyLibrary.BuildChains(out rejected);
            List<string> fresh = chains.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            if (fresh.SequenceEqual(this.chainNames, StringComparer.Ordinal) && !verbose)
            {
                return;
            }

            this.chainNames.Clear();
            this.chainNames.AddRange(fresh);

            // Значения ячеек запоминаются до подмены списка: у ячейки
            // выпадающего списка значение обязано быть среди его строк, иначе
            // таблица ругается на каждую отрисовку.
            List<string> chosen = new List<string>();
            foreach (DataGridViewRow row in this.spectraGrid.Rows)
            {
                chosen.Add(row.Cells[1].Value as string);
            }

            this.nuclideSetColumn.Items.Clear();
            // Первая строка — вся библиотека: в спектре ищутся линии всех
            // наборов, и каждый набор входит своей серией со своей свободной
            // активностью. Так работал прежний вариант, когда не отмечали
            // ничего, и это разумное умолчание — спектр не выпадает из счёта
            // только потому, что его забыли разметить. Разметка нужна, когда
            // известно, ЧТО в пробе: лишние наборы дают лишние серии, а слабая
            // серия отбрасывается по разбросу и тратит линии впустую.
            this.nuclideSetColumn.Items.Add(Resources.EfficiencyMakerWholeLibrary);
            foreach (string name in this.chainNames)
            {
                this.nuclideSetColumn.Items.Add(name);
            }

            for (int i = 0; i < this.spectraGrid.Rows.Count; i++)
            {
                // Набор мог исчезнуть из конфига, пока форма открыта. Строка не
                // теряется, но выбор в ней сбрасывается, и об этом говорится.
                bool known = !string.IsNullOrEmpty(chosen[i])
                             && this.nuclideSetColumn.Items.Contains(chosen[i]);
                this.spectraGrid.Rows[i].Cells[1].Value =
                    known ? chosen[i] : Resources.EfficiencyMakerWholeLibrary;
                if (!known && !string.IsNullOrEmpty(chosen[i]))
                {
                    AppendLog(string.Format(Resources.EfficiencyMakerSetGone, chosen[i]));
                }
            }

            if (!verbose)
            {
                return;
            }

            if (this.chainNames.Count == 0)
            {
                AppendLog(Resources.EfficiencyMakerNoChains);
            }

            foreach (EfficiencyLibrary.SetReject reject in rejected)
            {
                AppendLog(string.Format(Resources.EfficiencyMakerSetSkipped,
                                        reject.Name, reject.Reason));
            }
        }

        /// <summary>
        /// Список раскрывают — самое время перечитать наборы: соседнее окно
        /// могло завести новый, пока эта форма открыта.
        /// </summary>
        void spectraGrid_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (e.ColumnIndex == 1)
            {
                this.ReloadNuclideSets(false);
            }
        }

        /// <summary>
        /// Возврат в окно — тоже повод перечитать. Набор заводят в соседнем
        /// окне и возвращаются сюда; ждать, пока раскроют список, незачем, а
        /// подмена состава списка вне режима правки ячейки безопаснее.
        /// </summary>
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            this.ReloadNuclideSets(false);
        }

        /// <summary>
        /// Своя обработка вместо стандартного окна с ошибкой. Расхождение
        /// значения ячейки со списком — не повод показывать пользователю
        /// диалог с трассировкой; строка чинится на месте.
        /// </summary>
        void spectraGrid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            if (e.ColumnIndex == 1 && e.RowIndex >= 0 && e.RowIndex < this.spectraGrid.Rows.Count)
            {
                this.spectraGrid.Rows[e.RowIndex].Cells[1].Value =
                    Resources.EfficiencyMakerWholeLibrary;
            }

            e.ThrowException = false;
        }

        /// <summary>
        /// Набор, угаданный по имени файла: «ASN16_Th232.xml» -> «Th-232».
        ///
        /// Имя набора ищется в имени файла как подстрока, у обоих выброшены
        /// разделители: «Th-232» -> «th232» находится в «asn16th232». Совпадение
        /// короче трёх знаков не в счёт, и подойти должен РОВНО один набор —
        /// иначе остаётся вся библиотека. Пачку в двадцать файлов иначе
        /// размечать руками, а угадать неправильно хуже, чем не угадать: выбор
        /// виден в ячейке, но проверять его станут не все.
        /// </summary>
        string GuessChain(string path)
        {
            string name = Simplify(Path.GetFileNameWithoutExtension(path));
            string found = null;
            foreach (string chain in this.chainNames)
            {
                string token = Simplify(chain);
                if (token.Length >= 3 && name.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    if (found != null)
                    {
                        return Resources.EfficiencyMakerWholeLibrary;
                    }

                    found = chain;
                }
            }

            return found ?? Resources.EfficiencyMakerWholeLibrary;
        }

        static string Simplify(string value)
        {
            StringBuilder text = new StringBuilder();
            foreach (char c in value ?? "")
            {
                if (char.IsLetterOrDigit(c))
                {
                    text.Append(char.ToLowerInvariant(c));
                }
            }

            return text.ToString();
        }

        // ------------------------------------------------------------------
        // Ввод
        // ------------------------------------------------------------------


        void spectraAddButton_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = Resources.EfficiencyMakerSpectrumFilter;
                dialog.Multiselect = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                foreach (string file in dialog.FileNames)
                {
                    if (this.spectrumFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    this.spectrumFiles.Add(file);
                    int row = this.spectraGrid.Rows.Add(Path.GetFileNameWithoutExtension(file),
                                                        this.GuessChain(file));
                    // Полный путь живёт в строке, а не в ячейке: показывать его
                    // целиком незачем, а одинаковые имена в разных каталогах
                    // встречаются.
                    this.spectraGrid.Rows[row].Tag = file;
                }
            }
        }

        void spectraRemoveButton_Click(object sender, EventArgs e)
        {
            List<int> rows = new List<int>();
            foreach (DataGridViewCell cell in this.spectraGrid.SelectedCells)
            {
                if (!rows.Contains(cell.RowIndex))
                {
                    rows.Add(cell.RowIndex);
                }
            }

            foreach (int index in rows.OrderByDescending(i => i))
            {
                this.spectrumFiles.Remove((string)this.spectraGrid.Rows[index].Tag);
                this.spectraGrid.Rows.RemoveAt(index);
            }
        }

        void spectraClearButton_Click(object sender, EventArgs e)
        {
            this.spectrumFiles.Clear();
            this.spectraGrid.Rows.Clear();
        }


        void runButton_Click(object sender, EventArgs e)
        {
            if (Busy())
            {
                this.cancelRequested = true;
                return;
            }

            if (this.spectrumFiles.Count == 0)
            {
                MessageBox.Show(this, Resources.EfficiencyMakerNoSpectra, this.Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            EfficiencyFitInput input = BuildInput();
            if (input == null)
            {
                return;
            }

            Start(this.runButton, this.calculateButton, Resources.EfficiencyMakerRunning,
                  (log, cancelled) => EfficiencyFitter.Run(input, log, cancelled));
        }

        /// <summary>
        /// Второй путь к кривой: посчитать её из геометрии, а не восстановить из
        /// измерений. Спектры для этого не нужны вовсе — нужен файл геометрии.
        /// </summary>
        void calculateButton_Click(object sender, EventArgs e)
        {
            if (Busy())
            {
                this.cancelRequested = true;
                return;
            }

            // Геометрия берётся ИЗ ПОЛЕЙ редактора, а не из того, что когда-то
            // загрузили файлом. Иначе выбор готового детектора или правка руками
            // на расчёт не влияли: считалась бы прежняя геометрия, а результат
            // выглядел бы законным.
            if (!this.geometryPanel.TryCommit())
            {
                return;
            }

            this.geometry = this.geometryPanel.Model;
            if (this.geometry == null)
            {
                MessageBox.Show(this, Resources.EfficiencyMakerNoGeometry, this.Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            GeometryModel model = this.geometry;
            Start(this.calculateButton, this.runButton, Resources.EfficiencyMakerCalculating,
                  (log, cancelled) => EfficiencyCalculation.Run(
                      model, SimulationHistories, log, cancelled));
        }

        bool Busy()
        {
            return this.worker != null && this.worker.IsBusy;
        }

        /// <summary>
        /// Общая обвязка обоих прогонов: кнопка запуска становится «Стоп»,
        /// вторая гаснет, счёт идёт в фоне, отмена опрашивается заданием.
        /// </summary>
        void Start(Button trigger, Button other, string status,
                   Func<Action<string>, Func<bool>, EfficiencyFitResult> job)
        {
            // Журнал НЕ чистится — ни здесь, ни где-либо ещё, пока окно не
            // закрыли. Он общий на обе вкладки, и причины отбраковки наборов
            // нуклидов попадают в него при разборе набора, задолго до прогона;
            // очистка перед расчётом из геометрии стирала разбор, к которому
            // расчёт вообще не относится, и понять, почему набор не виден,
            // становилось не по чему.
            if (this.logTextBox.TextLength > 0)
            {
                AppendLog("");
            }

            this.cancelRequested = false;
            string caption = trigger.Text;
            trigger.Text = Resources.EfficiencyMakerStop;
            other.Enabled = false;
            this.saveButton.Enabled = false;
            this.exportButton.Enabled = false;
            this.progressBar.Visible = true;
            this.statusLabel.Text = status;

            // Язык интерфейса выставлен только на потоке формы (MainForm), а
            // счёт идёт на потоке BackgroundWorker: без переноса культуры все
            // строки прогона — причины отбраковки, итог фита, ошибки — брались
            // бы из нейтрального ресурса вместо выбранного языка. Культура
            // счёта переносится вместе с ней: MainForm подменяет в ней
            // десятичный разделитель на точку, а числа в лог печатает фиттер.
            CultureInfo ui = CultureInfo.CurrentUICulture;
            CultureInfo formatting = CultureInfo.CurrentCulture;
            bool otherWasEnabled = other == this.calculateButton
                ? this.geometry != null
                : true;

            this.worker = new BackgroundWorker { WorkerReportsProgress = true };
            this.worker.DoWork += (s, args) =>
            {
                Thread.CurrentThread.CurrentUICulture = ui;
                Thread.CurrentThread.CurrentCulture = formatting;
                BackgroundWorker self = (BackgroundWorker)s;
                args.Result = job(message => self.ReportProgress(0, message),
                                  () => this.cancelRequested);
            };
            this.worker.ProgressChanged += (s, args) => AppendLog((string)args.UserState);
            this.worker.RunWorkerCompleted += (s, args) =>
            {
                // Окно могли закрыть, не дожидаясь конца прогона: обработчик
                // придёт всё равно, уже после Dispose, и обращение к любому
                // контролу свалило бы приложение (своего обработчика
                // необработанных исключений у него нет).
                if (this.IsDisposed || this.Disposing)
                {
                    return;
                }

                this.progressBar.Visible = false;
                trigger.Text = caption;
                other.Enabled = otherWasEnabled;
                if (args.Error != null)
                {
                    this.statusLabel.Text = args.Error.Message;
                    AppendLog(args.Error.ToString());
                    return;
                }

                Finish((EfficiencyFitResult)args.Result);
            };
            this.worker.RunWorkerAsync();
        }

        EfficiencyFitInput BuildInput()
        {
            // Правка ячейки могла остаться незакрытой — без этого выбор в
            // последней тронутой строке в модель не попадёт.
            this.spectraGrid.EndEdit();

            // Пустой список спектров называется своим именем. Раньше он
            // доходил до счётчика наборов и получал «набор не выбран» — жалоба
            // не на то, чего не хватает.
            if (this.spectrumFiles.Count == 0)
            {
                MessageBox.Show(this, Resources.EfficiencyMakerNoSpectra, this.Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            EfficiencyFitInput input = new EfficiencyFitInput();
            input.SpectrumFiles.AddRange(this.spectrumFiles);
            int assigned = 0;
            foreach (DataGridViewRow row in this.spectraGrid.Rows)
            {
                string path = row.Tag as string;
                string chain = row.Cells[1].Value as string;
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                bool whole = string.IsNullOrEmpty(chain)
                             || chain == Resources.EfficiencyMakerWholeLibrary;
                input.ChainsBySpectrum[path] = whole
                    ? new List<string>(this.chainNames)
                    : new List<string> { chain };
                if (input.ChainsBySpectrum[path].Count > 0)
                {
                    assigned++;
                }
            }

            if (assigned == 0)
            {
                MessageBox.Show(this, Resources.EfficiencyMakerNoChainsChecked, this.Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            input.PolynomialOrder = (int)this.orderNumericUpDown.Value;
            input.MinIntensity = (double)this.minIntensityNumericUpDown.Value;
            input.MinSignificance = (double)this.minSignificanceNumericUpDown.Value;
            input.SubtractBackground = this.backgroundCheckBox.Checked;
            input.Reference = this.referenceCurve;

            double anchorEnergy, anchorEfficiency;
            if (TryParse(this.anchorEnergyTextBox.Text, out anchorEnergy)
                && TryParse(this.anchorEfficiencyTextBox.Text, out anchorEfficiency))
            {
                input.AnchorEnergy = anchorEnergy;
                input.AnchorEfficiency = anchorEfficiency;
            }

            return input;
        }

        static bool TryParse(string text, out double value)
        {
            text = (text ?? "").Trim().Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && value > 0.0;
        }

        void Finish(EfficiencyFitResult result)
        {
            this.lastResult = result;
            if (!string.IsNullOrEmpty(result.Error))
            {
                this.statusLabel.Text = result.Error;
                AppendLog(result.Error);
                this.graph.SetData(this.referenceCurve, null);
                return;
            }

            this.graph.SetData(this.referenceCurve, result);
            // Пересчитанная кривая — тоже правка: она ещё нигде не сохранена.
            if (result.Ok)
            {
                this.SetDirty();
            }

            this.UpdateSaveState();

            string level;
            switch (result.LevelSource)
            {
                case EfficiencyLevelSource.Reference:
                    level = Resources.EfficiencyMakerLevelReference;
                    break;
                case EfficiencyLevelSource.Anchor:
                    level = Resources.EfficiencyMakerLevelAnchor;
                    break;
                case EfficiencyLevelSource.Simulation:
                    level = Resources.EfficiencyMakerLevelSimulation;
                    break;
                default:
                    level = Resources.EfficiencyMakerLevelShapeOnly;
                    break;
            }

            // У расчёта из геометрии нет ни серий, ни χ²: там нечего подгонять,
            // и итог другой — сколько точек и в каком диапазоне.
            this.statusLabel.Text = result.LevelSource == EfficiencyLevelSource.Simulation
                ? string.Format(Resources.EfficiencyMakerCalcStatus, result.Curve.Count,
                                (int)result.MinEnergy, (int)result.MaxEnergy, level)
                : string.Format(Resources.EfficiencyMakerStatus,
                                result.AcceptedCount, result.SeriesKeys.Count,
                                result.Chi2Ndf, level);

            AppendLog("");
            AppendLog(this.statusLabel.Text);
            foreach (var group in result.Observations.GroupBy(o => o.SeriesKey))
            {
                AppendLog(group.Key);
                foreach (EfficiencyObservation o in group.OrderBy(o => o.Energy))
                {
                    AppendLog(string.Format(CultureInfo.InvariantCulture,
                        "    {0,8:F1} keV  I={1,6:F2}%  net={2,12:F0}  z={3,7:F1}  {4}",
                        o.Energy, o.Intensity, o.NetCounts, o.Significance,
                        o.Accepted
                            ? string.Format(CultureInfo.InvariantCulture,
                                "eps={0:E3}  d(ln)={1:F3}", o.MeasuredEfficiency, o.Residual)
                            : "- " + o.Reason));
                }
            }
        }

        /// <summary>
        /// Сохранить — значит положить в привязанную конфигурацию прибора.
        /// Другого места у кривой нет: окно открывается только из списка
        /// конфигураций эффективности и только с готовой конфигурацией.
        ///
        /// Кривой при этом может и не быть: правка геометрии сохраняется сама
        /// по себе, и кнопка на неё включается (см. UpdateSaveState). Здесь
        /// стояла проверка «нет кривой — выйти», и нажатие после правки одной
        /// геометрии не делало НИЧЕГО: кнопка доступна, звёздочка в заголовке
        /// не гаснет, сказать об этом некому.
        /// </summary>
        void saveButton_Click(object sender, EventArgs e)
        {
            if (this.boundConfig == null)
            {
                return;
            }

            if (this.SaveIntoConfig())
            {
                AppendLog(string.Format(Resources.EfficiencyMakerSavedToConfig,
                                        this.boundConfig.Name));
            }
        }

        void exportButton_Click(object sender, EventArgs e)
        {
            if (this.lastResult == null || !this.lastResult.Ok)
            {
                return;
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = Resources.EfficiencyMakerCsvFilter;
                dialog.FileName = "efficiency.csv";
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    EfficiencyFitter.ExportCsv(dialog.FileName, this.lastResult);
                    this.statusLabel.Text = string.Format(Resources.EfficiencyMakerSaved, dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// Отмена проверяется между спектрами, и прогон по пачке может идти
        /// ещё долго после закрытия окна. Держать окно до конца нельзя,
        /// поэтому оно закрывается сразу, счёт получает сигнал отмены, а
        /// запоздавшие обработчики гасятся проверкой IsDisposed.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            this.cancelRequested = true;
            base.OnFormClosing(e);
        }

        void AppendLog(string message)
        {
            // Строка добавляется, а не переписывается целиком: в отчёте по
            // пачке спектров строк тысячи, и присваивание Text на каждой из
            // них перестраивало весь текст заново — окно вставало.
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            this.logTextBox.AppendText((message ?? "") + Environment.NewLine);
        }
    }
}
