using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor
{
    /// <summary>
    /// Конструктор кривой эффективности регистрации.
    ///
    /// Кривая берётся из геометрии прибора и пробы монте-карловским переносом
    /// (<see cref="EfficiencyCalculation"/>): геометрия правится в редакторе на
    /// первой вкладке (или импортируется из файла LSRM `.in`), расчёт запускается со
    /// второй, результат ложится на график и сохраняется в привязанную
    /// конфигурацию эффективности прибора. Уровень кривой АБСОЛЮТНЫЙ — он
    /// следует из геометрии, а не подгоняется.
    ///
    /// Кривая привязана к прибору И геометрии: эффективность полного
    /// поглощения зависит от телесного угла и самопоглощения в пробе, поэтому
    /// у одной конфигурации прибора — одна геометрия и одна кривая.
    ///
    /// Второго пути — эмпирического восстановления кривой из пачки спектров по
    /// вековому равновесию (вкладка «Fit to measured spectra» и её движок) —
    /// с 13.09.2026 нет: снят по решению Amber (`AMBER25`, «Этот функционал
    /// нужно убрать. Его не должно остаться»).
    /// </summary>
    public partial class EfficiencyMakerForm : Form
    {
        /// <summary>
        /// Кривая привязанной конфигурации прибора (см. <see cref="BindTo"/>):
        /// график рисует её пунктиром рядом с только что посчитанной.
        /// </summary>
        List<ROIEfficiencyData> referenceCurve = null;
        EfficiencyFitResult lastResult;
        GeometryModel geometry;
        BackgroundWorker worker;
        volatile bool cancelRequested;

        public EfficiencyMakerForm()
        {
            InitializeComponent();
            BuildGeometryTab();
            BuildCalcOptions();
            // Считать есть по чему сразу: в редакторе всегда лежит геометрия —
            // либо заготовка, либо конфигурация, либо импортированный файл.
            // Прежде кнопку включал только импорт, и выбранный готовый детектор
            // посчитать было нельзя.
            this.calculateButton.Enabled = true;
            UpdateGeometryLayout();
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

        // ------------------------------------------------------------------
        // Параметры расчёта из геометрии
        // ------------------------------------------------------------------

        GroupBox calcOptionsGroup;

        /// <summary>Идёт `ApplyCalcOptions` — верх подаётся редактору без пересчёта сцены (`AMBER94`).</summary>
        bool restoringCalcOptions;

        InvariantNumericUpDown calcMinEnergyBox, calcMaxEnergyBox, calcPointsBox,
                      calcHistoriesBox, calcThreadsBox;

        ComboBox calcGridBox;

        const int CalcLabelWidth = 130;

        const int CalcFieldWidth = 90;

        /// <summary>
        /// Панель параметров расчёта. Собирается кодом, а не дизайнером, — как
        /// и вкладка геометрии выше: подписи тогда берутся прямо из общих
        /// ресурсов, где у них уже есть русская пара, без прохода
        /// `ApplyResources` по списку контролов.
        ///
        /// Наружу вынесены ЦЕНА счёта и сетка, на которой он ведётся, — то,
        /// чего программа знать не может: до какой энергии меряет прибор и
        /// сколько человек готов ждать. Ключи физики переноса остаются внутри
        /// (<see cref="EfficiencySimulator"/>): каждый из них калиброван
        /// сверкой с Geant4 и новой TCCFCALC, и свободной крутилкой в окне
        /// абсолютный уровень кривой превратился бы в подгоночный, а кривая,
        /// посчитанная чужой физикой, попала бы в конфигурацию прибора
        /// неотличимой от штатной.
        ///
        /// Умолчания — ровно то, чем считалось до появления полей: 40…3000 кэВ
        /// штатной сеткой в 34 точки, 200 000 историй, все ядра кроме одного.
        /// </summary>
        void BuildCalcOptions()
        {
            // AMBER38: параметры начинаются у верха вкладки после снятия подсказки.
            this.calcOptionsGroup = new GroupBox
            {
                Text = Resources.ResponseMatrixParameters,
                Location = new System.Drawing.Point(13, 12),
                Size = new System.Drawing.Size(790, 84),
                TabIndex = 5,
            };

            this.tabPageCalculate.Controls.Add(this.calcOptionsGroup);

            int c1 = 12;
            int c2 = c1 + CalcLabelWidth + CalcFieldWidth + 24;
            int c3 = c2 + CalcLabelWidth + CalcFieldWidth + 24;

            // Умолчание поля идёт за умолчанием расчёта (`E36`): нижняя
            // граница контрола и так 1 кэВ, менять её не пришлось.
            this.calcMinEnergyBox = CalcField(Resources.ResponseMatrixMinEnergy, c1, 22, 1, 5000, 5);
            this.calcMaxEnergyBox = CalcField(Resources.ResponseMatrixMaxEnergy, c2, 22, 10, 10000, 3000);

            this.calcOptionsGroup.Controls.Add(new Label
            {
                Text = Resources.EfficiencyMakerGrid,
                Location = new System.Drawing.Point(c3, 25),
                Size = new System.Drawing.Size(CalcLabelWidth, 18),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            });

            this.calcGridBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new System.Drawing.Point(c3 + CalcLabelWidth, 22),
                Size = new System.Drawing.Size(146, 21),
            };

            this.calcGridBox.Items.Add(Resources.EfficiencyMakerGridStandard);
            this.calcGridBox.Items.Add(Resources.EfficiencyMakerGridLogarithmic);
            this.calcGridBox.SelectedIndex = 0;
            this.calcGridBox.SelectedIndexChanged += this.CalcGridChanged;
            this.calcOptionsGroup.Controls.Add(this.calcGridBox);

            this.calcPointsBox = CalcField(Resources.EfficiencyMakerPoints, c1, 50, 2, 500, 34);
            this.calcHistoriesBox = CalcField(Resources.EfficiencyMakerHistoriesLabel, c2, 50,
                                              1000, 10000000, 200000);
            this.calcThreadsBox = CalcField(Resources.ResponseMatrixThreads, c3, 50,
                                            1, 64, Math.Max(1, Environment.ProcessorCount - 1));

            this.calcHistoriesBox.Increment = 50000;

            // ⛔ ГРУППИРОВКИ РАЗРЯДОВ НЕТ ВОВСЕ (`A244`, решение Amber
            // 05.09.2026). Здесь стояло `calcHistoriesBox.ThousandsSeparator =
            // true` — последняя группировка приложения, и бралась она у
            // КУЛЬТУРЫ ПОТОКА: на русской системе «200 000» неразрывными
            // пробелами, на английской «200,000». Ту же строку сняли у формы
            // матрицы отклика (`ResponseMatrixForm.Layout.cs`), и она осталась
            // здесь одна: замер `NumericCultureProbeF68` нашёл её как
            // единственную запятую печати под `en-US` (`A261`).

            // Число точек штатная сетка считает сама — поле при ней заперто, а
            // не игнорируется молча: выставленное и ни на что не влияющее число
            // читается как обещание.
            this.calcPointsBox.Enabled = false;

            this.calcMinEnergyBox.ValueChanged += this.CalcRangeChanged;
            this.calcMaxEnergyBox.ValueChanged += this.CalcRangeChanged;

            // (E27) Верх диапазона нужен редактору геометрии: по нему считаются
            // размеры готовых сцен съёмки в поле. Панель создаётся раньше этих
            // полей, поэтому значение подаётся здесь и потом на каждой правке.
            this.geometryPanel.SetSceneEnergy((double)this.calcMaxEnergyBox.Value);

            // Кнопка съезжает под панель — её место в дизайнере было занято
            // ещё до появления параметров.
            this.calculateButton.Location =
                new System.Drawing.Point(13, this.calcOptionsGroup.Bottom + 12);
        }

        InvariantNumericUpDown CalcField(string caption, int x, int y,
                                decimal min, decimal max, decimal value)
        {
            this.calcOptionsGroup.Controls.Add(new Label
            {
                Text = caption,
                Location = new System.Drawing.Point(x, y + 3),
                Size = new System.Drawing.Size(CalcLabelWidth, 18),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            });

            InvariantNumericUpDown box = new InvariantNumericUpDown
            {
                Location = new System.Drawing.Point(x + CalcLabelWidth, y),
                Size = new System.Drawing.Size(CalcFieldWidth, 20),
                Minimum = min,
                Maximum = max,
                DecimalPlaces = 0,
                Value = value,
            };

            this.calcOptionsGroup.Controls.Add(box);
            return box;
        }

        void CalcGridChanged(object sender, EventArgs e)
        {
            this.calcPointsBox.Enabled = this.calcGridBox.SelectedIndex == 1;
        }

        /// <summary>
        /// Верх ниже низа развести сразу, а не при запуске: расчёт всё равно
        /// раздвинул бы такой диапазон сам, и увидеть это человек смог бы уже
        /// только в журнале, посчитанным.
        /// </summary>
        void CalcRangeChanged(object sender, EventArgs e)
        {
            // Сначала отдать верх редактору геометрии (E27), и только потом
            // разводить границы: у разведения есть ранний выход, и за ним
            // подача осталась бы несделанной.
            this.geometryPanel.SetSceneEnergy((double)this.calcMaxEnergyBox.Value, !this.restoringCalcOptions);

            if (this.calcMaxEnergyBox.Value > this.calcMinEnergyBox.Value)
            {
                return;
            }

            if (sender == this.calcMinEnergyBox)
            {
                this.calcMaxEnergyBox.Value = Math.Min(this.calcMaxEnergyBox.Maximum,
                                                       this.calcMinEnergyBox.Value + 10m);
            }
            else
            {
                this.calcMinEnergyBox.Value = Math.Max(this.calcMinEnergyBox.Minimum,
                                                       this.calcMaxEnergyBox.Value - 10m);
            }
        }

        static void SetClamped(InvariantNumericUpDown box, decimal value)
        {
            box.Value = Math.Min(box.Maximum, Math.Max(box.Minimum, value));
        }

        /// <summary>
        /// (`AMBER201`, Р4, подозрение полосы 8) То же для числа из клейма или
        /// кривой: зажим В DOUBLE, до приведения. <c>(decimal)</c> от числа за
        /// 7.9e28 или от бесконечности бросает <c>OverflowException</c> сам, и
        /// прежде такая точка кривой (импортированной, правленой руками) роняла
        /// открытие конструктора из <see cref="BindTo"/>. NaN — поле не трогается.
        /// </summary>
        static void SetClamped(InvariantNumericUpDown box, double value)
        {
            if (double.IsNaN(value))
            {
                return;
            }

            if (value <= (double)box.Minimum)
            {
                box.Value = box.Minimum;
            }
            else if (value >= (double)box.Maximum)
            {
                box.Value = box.Maximum;
            }
            else
            {
                SetClamped(box, (decimal)value);
            }
        }

        /// <summary>
        /// (E23) Восстановить в полях расчёта то, чем кривая была посчитана В
        /// ПРОШЛЫЙ РАЗ. Возвращает строку для журнала; пусто — восстанавливать
        /// нечего, поля остаются заводскими.
        ///
        /// До 16.08.2026 поля жили литералами конструктора (40 кэВ, 3000, 34
        /// узла, 200 000 историй), и открытая на правку геометрия получала их
        /// заново: кривую строили от 20 кэВ, а при следующем открытии
        /// предлагалось 40. Прежние значения при этом не терялись — клеймо
        /// <see cref="EfficiencyConfigData.ComputeStamp"/> (E12) хранит их все,
        /// а края несёт и сама кривая, — их просто никто не читал обратно.
        ///
        /// Порядок источников: сперва клеймо (там ВСЕ параметры), при его
        /// отсутствии — края кривой (у кривой, восстановленной по измерениям,
        /// клейма нет по построению, но диапазон, в котором прибор описан, есть
        /// и там). Потоки не восстанавливаются НАРОЧНО: это свойство машины, а
        /// не постановки задачи, и число ядер у другого хозяина файла другое.
        ///
        /// Тем же приёмом и по тому же доводу живёт `ResponseMatrixForm.
        /// ApplyCurveRange` (E18 «а») — с одной разницей: там переносятся
        /// ТОЛЬКО границы, потому что матрица — другая задача со своей ценой
        /// счёта, а здесь задача та же, пересчитать ту же кривую.
        /// </summary>
        string ApplyCalcOptions(EfficiencyConfigData config)
        {
            if (config == null)
            {
                return "";
            }

            double lo, hi, histories, nodes;
            bool logGrid;
            if (TryParseComputeStamp(config.ComputeStamp, out lo, out hi,
                                     out histories, out nodes, out logGrid))
            {
                SetClamped(this.calcMinEnergyBox, lo);
                SetClamped(this.calcMaxEnergyBox, hi);
                if (histories > 0.0)
                {
                    SetClamped(this.calcHistoriesBox, histories);
                }

                this.calcGridBox.SelectedIndex = logGrid ? 1 : 0;
                if (logGrid && nodes > 0.0)
                {
                    SetClamped(this.calcPointsBox,
                               (double)this.OrderedNodes(lo, hi, (int)Math.Min(nodes, 100000.0)));
                }

                return string.Format(CultureInfo.InvariantCulture,
                                     Resources.EfficiencyMakerCalcRestored, config.ComputeStamp);
            }

            if (!CurveRange(config, out lo, out hi))
            {
                return "";
            }

            SetClamped(this.calcMinEnergyBox, lo);
            SetClamped(this.calcMaxEnergyBox, hi);
            return string.Format(CultureInfo.InvariantCulture,
                                 Resources.EfficiencyMakerRangeFromCurve, lo, hi);
        }

        /// <summary>
        /// (`AMBER201`, Р4, мелочь 8.7) Сколько узлов было ЗАКАЗАНО, если
        /// посчитано <paramref name="computed"/>. Клеймо кривой пишет
        /// `grid=…/N log` числом ПОСЧИТАННЫХ узлов (длиной кривой), а сетка
        /// добавляет к заказанным свои: сгущение ниже 40 кэВ (`AMBER95`) и узлы
        /// у K-краёв веществ геометрии (E24). Прежде N ложилось в поле «Точек»
        /// как есть, и каждое «открыть — посчитать — сохранить» растило сетку на
        /// эти добавки. Здесь берётся наибольшее заказанное n ≤ N, при котором
        /// та же сетка на той же геометрии даёт не больше N узлов, — то есть
        /// прежний заказ, если сетка с тех пор не менялась. Клеймо и сама кривая
        /// не трогаются.
        /// </summary>
        int OrderedNodes(double lo, double hi, int computed)
        {
            if (computed <= 2)
            {
                return computed;
            }

            try
            {
                for (int n = computed; n >= 2; n--)
                {
                    EfficiencyCalculationOptions probe = new EfficiencyCalculationOptions
                    {
                        MinEnergyKev = lo,
                        MaxEnergyKev = hi,
                        GridMode = EfficiencyGridMode.Logarithmic,
                        NodeCount = n,
                    };
                    if (probe.BuildGrid(this.geometry, null).Length <= computed)
                    {
                        return n;
                    }
                }
            }
            catch (Exception)
            {
                // Сетку не построить (геометрия негодна) — прежнее поведение.
            }

            return computed;
        }

        /// <summary>
        /// Разобрать клеймо `phys=6; hist=200000; grid=20-3000 keV/34 std`.
        /// Ложь — клейма нет или в нём нет диапазона; тогда лучше оставить поля
        /// как есть, чем подставить половину разобранного.
        ///
        /// Клеймо пишется <see cref="CultureInfo.InvariantCulture"/> и читается
        /// ею же: у хозяина файла с запятой в качестве разделителя дробной
        /// части `20.5` иначе разобралось бы в 205.
        /// </summary>
        static bool TryParseComputeStamp(string stamp, out double lo, out double hi,
                                         out double histories, out double nodes, out bool logGrid)
        {
            lo = hi = histories = nodes = 0.0;
            logGrid = false;
            if (string.IsNullOrEmpty(stamp))
            {
                return false;
            }

            Match grid = Regex.Match(stamp,
                @"grid=\s*([0-9.]+)\s*-\s*([0-9.]+)\s*keV\s*/\s*([0-9]+)\s*(std|log)",
                RegexOptions.IgnoreCase);
            if (!grid.Success
                || !double.TryParse(grid.Groups[1].Value, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out lo)
                || !double.TryParse(grid.Groups[2].Value, NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out hi)
                || hi <= lo)
            {
                return false;
            }

            double parsed;
            if (double.TryParse(grid.Groups[3].Value, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out parsed))
            {
                nodes = parsed;
            }

            logGrid = string.Equals(grid.Groups[4].Value, "log", StringComparison.OrdinalIgnoreCase);

            Match hist = Regex.Match(stamp, @"hist=\s*([0-9]+)", RegexOptions.IgnoreCase);
            if (hist.Success && double.TryParse(hist.Groups[1].Value, NumberStyles.Float,
                                                CultureInfo.InvariantCulture, out parsed))
            {
                histories = parsed;
            }

            return true;
        }

        /// <summary>
        /// Края кривой конфигурации, кэВ. Минимум и максимум, а не первая и
        /// последняя точка: порядок списка — соглашение, а не проверяемое
        /// свойство (тот же довод, что в <c>ResponseMatrixForm.CurveRange</c>).
        /// </summary>
        static bool CurveRange(EfficiencyConfigData config, out double lo, out double hi)
        {
            lo = double.MaxValue;
            hi = double.MinValue;
            if (config == null || !config.HasCurve)
            {
                return false;
            }

            bool any = false;
            foreach (ROIEfficiencyData point in config.Curve)
            {
                if (point == null || point.Energy <= 0.0)
                {
                    continue;
                }

                lo = Math.Min(lo, point.Energy);
                hi = Math.Max(hi, point.Energy);
                any = true;
            }

            return any && hi > lo;
        }

        /// <summary>Параметры расчёта, как они выставлены в полях.</summary>
        EfficiencyCalculationOptions CurrentCalcOptions()
        {
            return new EfficiencyCalculationOptions
            {
                MinEnergyKev = (double)this.calcMinEnergyBox.Value,
                MaxEnergyKev = (double)this.calcMaxEnergyBox.Value,
                GridMode = this.calcGridBox.SelectedIndex == 1
                    ? EfficiencyGridMode.Logarithmic
                    : EfficiencyGridMode.Standard,
                NodeCount = (int)this.calcPointsBox.Value,
                Histories = (int)this.calcHistoriesBox.Value,
                Threads = (int)this.calcThreadsBox.Value,
            };
        }

        /// <summary>
        /// «Импорт из LSRM…» редактора геометрии. Файл `.in` — геометрия в поля
        /// редактора; `.txt` (экспорт `EffCalcMC`), `.efa`, `.efr` — кривая
        /// эффективности окна (`AMBER226`, П330: Amber 15.10.2026, дословно:
        /// «Нужно добавить в диалог открытия файла в редакторе геометрий: Импорт
        /// кривых интенсивности из LSRM в редактор геометрий. Помимо txt формата,
        /// нужно поддержать формат файлов efa и efr»). Различает их расширение;
        /// всё прочее («Все файлы») читается геометрией, как и до П330.
        /// </summary>
        void ImportGeometryClick(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                // Фильтр свой, а не `EfficiencyMakerGeometryFilter`: тот же
                // ресурс несёт второй шаг импорта кривой на вкладке прибора, где
                // спрашивают именно `.in`.
                dialog.Filter = ResourceText("EfficiencyMakerLsrmImportFilter",
                    "LSRM files (*.in;*.txt;*.efa;*.efr)|*.in;*.txt;*.efa;*.efr"
                    + "|LSRM geometry (*.in)|*.in"
                    + "|LSRM efficiency curve (*.txt;*.efa;*.efr)|*.txt;*.efa;*.efr"
                    + "|All files (*.*)|*.*");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                if (LsrmEfficiencyFile.IsCurveFile(dialog.FileName))
                {
                    string problem;
                    if (!this.ImportCurveFile(dialog.FileName, sets => PickLsrmSet(this, sets), out problem)
                        && problem != null)
                    {
                        MessageBox.Show(this,
                            string.Format(Resources.ERRFileOpenFailure, dialog.FileName, problem),
                            this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }

                    return;
                }

                try
                {
                    GeometryModel model = GeometryModel.Load(dialog.FileName);
                    // (`AMBER201`, Р4, мелочь G.5) Чужой файл — отказ, а не
                    // заготовка из нулей. Читатель `.in` — разбор «ключ =
                    // значение» и на любом тексте не падает: прежде картинка,
                    // CSV или `.in` коаксиального германия (`DC_*`) въезжали в
                    // поля кристаллом нулевых размеров, и журнал говорил
                    // «геометрия загружена». Признак — ни одного ключа или нет
                    // размеров сцинтиллятора (тот же, что был у снятого с вкладки
                    // прибора импорта ЛСРМ, `AMBER227`).
                    if (!model.HasScintillatorCrystal())
                    {
                        MessageBox.Show(this,
                            string.Format(CultureInfo.InvariantCulture,
                                ResourceText("EfficiencyMakerGeometryNotScintillatorFile",
                                    "{0}: no scintillator crystal dimensions found (DS_CrystalDiameter / DS_CrystalHeight"
                                    + " or DS_CrystalBoxX / Y / Z) - this is not a scintillator geometry file. Nothing was loaded."),
                                dialog.FileName),
                            this.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    this.geometryPanel.SetModel(model);
                    this.geometry = model;
                    this.calculateButton.Enabled = true;
                    AppendLog(string.Format(CultureInfo.InvariantCulture,
                                            Resources.EfficiencyMakerGeometryLoaded, model.Describe()));
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

        /// <summary>
        /// (`AMBER226`, П330) Кривая ЛСРМ из файла — КРИВОЙ ОКНА: рисуется
        /// вместо посчитанной, «Сохранить» кладёт её в конфигурацию вместе с
        /// геометрией из полей (происхождение LSRM), «Посчитать» заменяет её
        /// своей. Решения Amber 15.10.2026 вопросником, дословно: «Кривая окна
        /// (Рекомендую)»; из `.efa` — «Подгонку ЛСРМ (Рекомендую)»; из `.efr` —
        /// «Точки как есть (Рекомендую)»; шапка файла — «Только в журнал
        /// (Рекомендую)»: поля геометрии импорт кривой НЕ трогает.
        ///
        /// Окна не поднимает (кроме выбора геометрии через
        /// <paramref name="choose"/>, если их в файле несколько) — его зовёт
        /// проба. Ложь с <paramref name="problem"/> == null — человек отказался
        /// от выбора; с причиной — файл негоден, кривая окна прежняя.
        /// </summary>
        /// <param name="choose">индекс геометрии из нескольких; −1 — отказ</param>
        internal bool ImportCurveFile(string path, Func<IList<LsrmCurveSet>, int> choose, out string problem)
        {
            problem = null;
            if (Busy())
            {
                // Счёт, закончившись, положил бы свою кривую поверх импортированной.
                problem = ResourceText("EfficiencyMakerLsrmBusy",
                    "A calculation is running - stop it before importing a curve.");
                return false;
            }

            string file = System.IO.Path.GetFileName(path);
            List<ROIEfficiencyData> curve;
            List<string> log = new List<string>();
            string note;
            string summary;
            if (!LsrmEfficiencyFile.IsApproximation(path)
                && string.Equals(System.IO.Path.GetExtension(path), ".txt", StringComparison.OrdinalIgnoreCase))
            {
                // Экспорт `EffCalcMC.txt` — тем же читателем, что импорт на вкладке
                // прибора (`AMBER13`): шапка проверяется, точки за 100 % отсекаются.
                curve = DeviceConfigForm.ReadLsrmEfficiencyExport(path, out problem);
                if (problem != null)
                {
                    return false;
                }

                summary = string.Format(CultureInfo.InvariantCulture,
                    ResourceText("EfficiencyMakerLsrmCurveExport", "Curve: {0} point(s) of the LSRM EffCalcMC export."),
                    curve.Count);
                note = string.Format(CultureInfo.InvariantCulture, "LSRM export: {0}", file);
            }
            else
            {
                List<LsrmCurveSet> sets = LsrmEfficiencyFile.Read(path, out problem);
                if (sets == null)
                {
                    return false;
                }

                int index = 0;
                if (sets.Count > 1)
                {
                    index = choose == null ? -1 : choose(sets);
                    if (index < 0 || index >= sets.Count)
                    {
                        return false;
                    }
                }

                LsrmCurveSet set = sets[index];
                bool fromFit = LsrmEfficiencyFile.IsApproximation(path);
                LsrmCurveReport report;
                curve = LsrmEfficiencyFile.BuildCurve(set, fromFit, path, out report, out problem);
                if (curve == null)
                {
                    return false;
                }

                log.Add(string.Format(CultureInfo.InvariantCulture,
                    ResourceText("EfficiencyMakerLsrmGeometry", "LSRM file: {0}; geometry: {1}"),
                    file, set.Title));
                log.Add(ResourceText("EfficiencyMakerLsrmHeader",
                    "File header (into this log only - the geometry fields are not changed):"));
                foreach (KeyValuePair<string, string> pair in set.Header)
                {
                    string value = pair.Value ?? "";
                    log.Add("  " + pair.Key + " = " + (value.Length > 200 ? value.Substring(0, 200) + "..." : value));
                }

                if (set.Sources.Count > 0)
                {
                    log.Add(string.Format(CultureInfo.InvariantCulture,
                        ResourceText("EfficiencyMakerLsrmSources", "Sources ({0}): {1}"),
                        set.Sources.Count, string.Join("; ", set.Sources)));
                }

                log.Add(string.Format(CultureInfo.InvariantCulture,
                    ResourceText("EfficiencyMakerLsrmPoints", "Measured points of the file ({0}):"),
                    set.Points.Count));
                foreach (LsrmMeasuredPoint point in set.Points.OrderBy(p => p.Energy))
                {
                    log.Add(string.Format(CultureInfo.InvariantCulture, "  {0,9:f3} keV  {1,-12:G6} ±{2,6:f2} %  {3}{4}{5}",
                        point.Energy, point.Efficiency, point.ErrorPercent, point.Nuclide,
                        point.Source.Length > 0 ? "  [" + point.Source + "]" : "",
                        point.Excluded
                            ? "  " + ResourceText("EfficiencyMakerLsrmExcluded", "(excluded by LSRM: No)")
                            : ""));
                }

                if (fromFit)
                {
                    summary = string.Format(CultureInfo.InvariantCulture,
                        ResourceText("EfficiencyMakerLsrmCurveFit",
                            "Curve: the LSRM fit, {0} zone(s), {1} nodes {2:f1}...{3:f1} keV; overlapping zones"
                            + " are joined by a linear weight in lg E; nothing beyond the zones."),
                        report.Zones, curve.Count, curve[0].Energy, curve[curve.Count - 1].Energy);
                    if (report.GapNodes > 0)
                    {
                        summary += Environment.NewLine + string.Format(CultureInfo.InvariantCulture,
                            ResourceText("EfficiencyMakerLsrmGap",
                                "  {0} grid node(s) fell into a gap between zones - the curve has no value there."),
                            report.GapNodes);
                    }

                    note = string.Format(CultureInfo.InvariantCulture,
                        "LSRM fit (.efa): {0}; [{1};{2}]; {3} zone(s), {4} nodes",
                        file, set.Detector, set.Geometry, report.Zones, curve.Count);
                }
                else
                {
                    summary = string.Format(CultureInfo.InvariantCulture,
                        ResourceText("EfficiencyMakerLsrmCurvePoints",
                            "Curve: {0} measured point(s); excluded by LSRM (No): {1}; over {2:f0} % uncertainty: {3};"
                            + " same energy merged with 1/sigma^2 weights: {4}."),
                        curve.Count, report.ExcludedByLsrm, DeviceConfigForm.LsrmMaxErrorPercent,
                        report.OverMaxError, report.MergedDuplicates);
                    note = string.Format(CultureInfo.InvariantCulture,
                        "LSRM points (.efr): {0}; [{1};{2}]; {3} point(s) from {4} source(s), {5} excluded by LSRM, {6} merged",
                        file, set.Detector, set.Geometry, curve.Count, set.Sources.Count,
                        report.ExcludedByLsrm, report.MergedDuplicates);
                }
            }

            note += "; geometry: editor fields of the curve designer";
            EfficiencyFitResult result = new EfficiencyFitResult
            {
                Curve = curve,
                MinEnergy = curve.Min(p => p.Energy),
                MaxEnergy = curve.Max(p => p.Energy),
                Origin = EfficiencyOrigin.Lsrm,
                ImportNote = note,
            };

            this.lastResult = result;
            this.graph.SetData(this.referenceCurve, result);
            this.SetDirty();
            this.UpdateSaveState();

            if (this.logTextBox.TextLength > 0)
            {
                AppendLog("");
            }

            foreach (string line in log)
            {
                AppendLog(line);
            }

            AppendLog(summary);
            AppendLog(ResourceText("EfficiencyMakerLsrmSaveHint",
                "Save puts this curve into the configuration with the geometry of the editor fields (origin: LSRM);"
                + " Calculate replaces it with a computed one."));
            this.statusLabel.Text = string.Format(CultureInfo.InvariantCulture,
                ResourceText("EfficiencyMakerLsrmImported", "LSRM curve imported: {0} points, {1:f1}...{2:f1} keV - {3}"),
                curve.Count, result.MinEnergy, result.MaxEnergy, file);
            AppendLog(this.statusLabel.Text);

            // График с журналом живут на вкладке расчёта, а на вкладке редактора
            // спрятаны (`UpdateGeometryLayout`): без перехода импорт кривой
            // выглядел бы не сделавшим ничего.
            this.tabControl.SelectedTab = this.tabPageCalculate;
            return true;
        }

        /// <summary>
        /// (`AMBER226`) Выбор геометрии сводного файла ЛСРМ (в `…_2024.efa` их
        /// четыре). −1 — отказ.
        /// </summary>
        static int PickLsrmSet(IWin32Window owner, IList<LsrmCurveSet> sets)
        {
            using (Form form = new Form
            {
                Text = ResourceText("EfficiencyMakerLsrmPickTitle", "Import LSRM curve"),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false,
                ShowInTaskbar = false,
                ClientSize = new System.Drawing.Size(520, 260),
            })
            {
                Label prompt = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 34,
                    Padding = new Padding(8, 8, 8, 0),
                    Text = string.Format(CultureInfo.InvariantCulture,
                        ResourceText("EfficiencyMakerLsrmPickPrompt",
                            "The file holds {0} geometries. Which curve to import?"),
                        sets.Count),
                };

                ListBox list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
                foreach (LsrmCurveSet set in sets)
                {
                    list.Items.Add(string.Format(CultureInfo.InvariantCulture,
                        ResourceText("EfficiencyMakerLsrmPickItem", "{0} - {1} point(s), {2} zone(s)"),
                        set.Title, set.Points.Count, set.Zones.Count));
                }

                list.SelectedIndex = 0;

                Button ok = new Button
                {
                    Text = ResourceText("EfficiencyMakerLsrmPickOk", "Import"),
                    DialogResult = DialogResult.OK,
                    Size = new System.Drawing.Size(90, 26),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                };
                Button cancel = new Button
                {
                    Text = ResourceText("EfficiencyMakerLsrmPickCancel", "Cancel"),
                    DialogResult = DialogResult.Cancel,
                    Size = new System.Drawing.Size(90, 26),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                };

                // Ширина панели — сразу ширина окна: кнопки привязаны к правому
                // краю, и панель умолчательной ширины (200) при растяжке доком
                // уводила их за край окна (проверка экраном П330).
                Panel buttons = new Panel { Dock = DockStyle.Bottom, Height = 40, Width = form.ClientSize.Width };
                // Ширина кнопки — по подписи: русское «Импортировать» в 90
                // точек не помещалось (проверка экраном П330).
                foreach (Button button in new[] { ok, cancel })
                {
                    button.Width = Math.Max(90, TextRenderer.MeasureText(button.Text, form.Font).Width + 24);
                }

                cancel.Location = new System.Drawing.Point(form.ClientSize.Width - cancel.Width - 8, 7);
                ok.Location = new System.Drawing.Point(cancel.Left - ok.Width - 8, 7);
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);

                Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 8, 0) };
                body.Controls.Add(list);

                form.Controls.Add(body);
                form.Controls.Add(buttons);
                form.Controls.Add(prompt);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                list.DoubleClick += delegate
                {
                    if (list.SelectedIndex >= 0)
                    {
                        form.DialogResult = DialogResult.OK;
                    }
                };

                return form.ShowDialog(owner) == DialogResult.OK ? list.SelectedIndex : -1;
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
        /// исходную кривую — ту, что график рисует пунктиром рядом с
        /// посчитанной. Раньше её выбирали ROI-файлом; выбирать больше нечего,
        /// кривая своя.
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

            // Геометрии может не быть — так открывается и новая конфигурация, и
            // старая кривая, у которой геометрии нет (импортирована или восстановлена
            // по измерениям до 13.09.2026). Тогда в поля заезжает
            // ЗАГОТОВКА (SetModel(null) — сцинтиллятор в типичной обвязке), и
            // считать разрешено сразу: расчёт всё равно берёт геометрию из полей,
            // а не из того, что когда-то загрузили. Запертая кнопка означала бы,
            // что заполнить два десятка полей можно, а нажать «Посчитать» нельзя.
            this.geometryPanel.SetModel(config.Geometry);
            this.geometry = this.geometryPanel.Model;
            this.calculateButton.Enabled = true;

            // Подсказка разрешения из ПШПВ-калибровки прибора (E14): у
            // геометрии из редактора FwhmAt662Percent нулевой, а с нулём
            // допуск пика нулевой и поправка SingleScatter не даёт ничего.
            this.geometryPanel.SetFwhmSuggestion(FwhmPercentAt662(device));

            // Кривая конфигурации становится исходной: график рисует её
            // пунктиром рядом с посчитанной.
            this.referenceCurve = config.HasCurve ? config.Curve : null;
            this.graph.SetData(this.referenceCurve, this.lastResult);

            // (E23) Поля расчёта — тем, чем эта кривая была посчитана, а не
            // заводским. И ВСЛУХ: молча подменённый диапазон неотличим от
            // выбранного человеком, а именно на этом и попались — кривую
            // строили от 20 кэВ, при следующем открытии предлагалось 40.
            // (`AMBER94`) Восстановление верха — не правка человека: открытая
            // полевая сцена под него НЕ пересчитывается.
            string restored;
            this.restoringCalcOptions = true;
            try
            {
                restored = this.ApplyCalcOptions(config);
            }
            finally
            {
                this.restoringCalcOptions = false;
            }

            if (!string.IsNullOrEmpty(restored))
            {
                AppendLog(restored);
            }
            this.dirty = false;
            this.savedIntoBound = false;
            this.UpdateTitle();
            this.UpdateSaveState();
        }

        /// <summary>
        /// (`AMBER184`) Что-нибудь из этого окна уже легло в привязанную
        /// конфигурацию («Сохранить» конструктора) — с последней привязки.
        /// Нужно перепривязке: если прежний клон конфигурации отброшен ответом
        /// «Нет», сохранённое туда пропало вместе с ним, и окно снова держит
        /// несохранённое.
        /// </summary>
        bool savedIntoBound;

        /// <summary>
        /// (`AMBER184`) Конструктор положил кривую или геометрию в привязанную
        /// конфигурацию: форма прибора помечает свой клон изменённым СРАЗУ, а
        /// не только при закрытии окна.
        /// </summary>
        public event EventHandler SavedIntoConfig;

        /// <summary>Конфигурация прибора, к которой привязано окно.</summary>
        public DeviceConfigInfo BoundDevice
        {
            get { return this.boundDevice; }
        }

        /// <summary>Кривая, к которой привязано окно.</summary>
        public EfficiencyConfigData BoundConfig
        {
            get { return this.boundConfig; }
        }

        /// <summary>
        /// (`AMBER184`) Есть ли в окне то, что пропадёт с ним: правка, не
        /// положенная в конфигурацию, посчитанная кривая, идущий счёт или
        /// сохранённое в клон, который сейчас отбрасывается.
        /// </summary>
        public bool HasUnsavedWork
        {
            get
            {
                return this.dirty || this.savedIntoBound || Busy()
                       || (this.lastResult != null && this.lastResult.Ok);
            }
        }

        /// <summary>
        /// (`AMBER184`, 05.10.2026; решение Amber вопросником, дословно: «Не
        /// закрывать (Рекомендую)») Перепривязать окно к СВЕЖЕМУ клону той же
        /// конфигурации прибора — ответ «Нет» на вопрос о сохранении отбрасывает
        /// клон, к которому окно было привязано. Прежде окно в этом случае
        /// закрывалось, и счёт, несохранённая геометрия и кривая пропадали.
        ///
        /// Поля геометрии, посчитанная кривая, журнал и идущий счёт остаются
        /// как есть — это работа человека, а не содержимое конфигурации. Меняются
        /// только «куда сохранять» и пунктир исходной кривой. Если «Сохранить»
        /// этого окна уже клало что-то в отброшенный клон, оно пропало вместе с
        /// ним, — окно снова помечается несохранённым, и «Сохранить» доступна.
        /// </summary>
        public void Rebind(DeviceConfigInfo device, EfficiencyConfigData config)
        {
            bool lost = this.savedIntoBound;
            this.boundDevice = device;
            this.boundConfig = config;
            this.savedIntoBound = false;
            this.referenceCurve = config != null && config.HasCurve ? config.Curve : null;
            this.graph.SetData(this.referenceCurve,
                               this.lastResult != null && this.lastResult.Ok ? this.lastResult : null);
            if (lost)
            {
                this.dirty = true;
            }

            this.UpdateTitle();
            if (!Busy())
            {
                this.UpdateSaveState();
            }
        }

        /// <summary>
        /// Разрешение прибора на 662 кэВ, % — из ПШПВ-калибровки его настроек
        /// поиска пиков сквозь энергетическую калибровку (ПШПВ там в КАНАЛАХ).
        /// Ноль — калибровки нет или она не отвечает числом; подсказки тогда
        /// не будет, и это правильнее выдуманного числа.
        /// </summary>
        static double FwhmPercentAt662(DeviceConfigInfo device)
        {
            FWHMPeakDetectionMethodConfig fwhmConfig =
                device == null ? null : device.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig;
            FwhmCalibration fwhm = fwhmConfig == null ? null : fwhmConfig.FwhmCalibration;
            EnergyCalibration energy = device == null ? null : device.EnergyCalibration;
            if (fwhm == null || energy == null)
            {
                return 0.0;
            }

            try
            {
                double channel = energy.EnergyToChannel(662.0, device.NumberOfChannels);
                double fwhmChannels = fwhm.ChannelToFwhm(channel);
                if (!(fwhmChannels > 0.0) || double.IsNaN(fwhmChannels))
                {
                    return 0.0;
                }

                double fwhmKev = energy.ChannelToEnergy(channel + fwhmChannels / 2.0)
                                 - energy.ChannelToEnergy(channel - fwhmChannels / 2.0);
                return fwhmKev > 0.0 ? fwhmKev / 662.0 * 100.0 : 0.0;
            }
            catch (Exception)
            {
                return 0.0;
            }
        }

        /// <summary>
        /// (`AMBER227`, П331) Почему кривую с этим происхождением нельзя сохранить
        /// с этой геометрией; null — можно. Кривая ЛСРМ — доля квантов
        /// источника, а сцена поля (ISO) нормирована на единичный флюенс
        /// (<see cref="ResponseMatrix.NormalizationOf"/>): у такой пары нет
        /// масштаба дозы, а отказ дозы «пересчитайте кривую из геометрии» для
        /// кривой ЛСРМ невыполним. Запрет жил в импорте ЛСРМ на вкладке прибора
        /// (`ReadLsrmGeometry`, `AMBER18`); с её снятием кривая ЛСРМ заводится
        /// только здесь, и запрет переехал сюда. Без окна — его зовёт проба.
        /// </summary>
        internal static string LsrmCurveGeometryProblem(EfficiencyOrigin origin, GeometryModel geometry)
        {
            if (origin != EfficiencyOrigin.Lsrm
                || ResponseMatrix.NormalizationOf(geometry) != ResponseMatrixNormalization.PerUnitFluence)
            {
                return null;
            }

            return ResourceText("EfficiencyMakerLsrmFieldScene",
                "The LSRM curve cannot be saved with a field scene (ISO): the LSRM efficiency is per emitted quantum,"
                + " not per unit fluence, and the dose rate would have no scale. Choose a source scene for this curve,"
                + " or calculate the curve from this geometry.");
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

            // (`AMBER227`) Кривая ЛСРМ и сцена поля — отказ ДО записи: ни
            // кривая, ни геометрия не ложатся, окно остаётся несохранённым.
            // Происхождение — той кривой, что ОКАЖЕТСЯ в конфигурации: кривой
            // окна, а без неё — уже сохранённой (правка одной геометрии на ISO
            // под сохранённой кривой ЛСРМ дала бы ту же пару).
            bool freshCurve = this.lastResult != null && this.lastResult.Ok;
            if (freshCurve || this.boundConfig.HasCurve)
            {
                string pairProblem = LsrmCurveGeometryProblem(
                    freshCurve ? this.lastResult.Origin : this.boundConfig.Origin, this.geometryPanel.Model);
                if (pairProblem != null)
                {
                    AppendLog(pairProblem);
                    // Общая дверь, а не MessageBox: метод зовёт отражением проба
                    // (`LsrmGeometryImportProbe`), и в безоконном прогоне окно
                    // повесило бы её (сторож `check_headless`, `S100`).
                    AppUi.Report(pairProblem, this.Text, MessageBoxIcon.Warning);
                    return false;
                }
            }

            // (`AMBER201`, Р4, мелочь 8.8) КОПИЯ, а не модель панели. Прежде
            // конфигурация и редактор держали один объект: панель правит свою
            // модель на месте (выбор вида источника ставит `Scene` прямо в неё),
            // и после первого «Сохранить» такая правка без второго «Сохранить»
            // уже сидела в конфигурации прибора и уходила на диск с её
            // сохранением.
            this.boundConfig.Geometry = this.geometryPanel.Model.Clone();
            if (this.lastResult != null && this.lastResult.Ok)
            {
                List<ROIEfficiencyData> curve = new List<ROIEfficiencyData>();
                foreach (ROIEfficiencyData point in this.lastResult.Curve)
                {
                    curve.Add(point.Clone());
                }

                this.boundConfig.Curve = curve;
                // Кривую здесь даёт расчёт из геометрии (`AMBER25`, 13.09.2026:
                // прежняя развилка по `LevelSource` на `EfficiencyOrigin.Measurement`
                // ушла вместе с фитом) либо импорт файла ЛСРМ (`AMBER226`).
                this.boundConfig.Origin = this.lastResult.Origin;
                // Клеймо едет вместе с кривой (E12); у импорта оно пустое —
                // кривую считали не мы.
                this.boundConfig.ComputeStamp = this.lastResult.ComputeStamp ?? "";
                if (this.lastResult.Origin == EfficiencyOrigin.Lsrm)
                {
                    // (`AMBER226`) Кривая ЛСРМ относится к геометрии, которую
                    // человек задал полями к ней (у `.efa`/`.efr` файла `.in` нет)
                    // — отпечаток той, что сохраняется, как у импорта с `.in` на
                    // вкладке прибора (`AMBER18`/`AMBER185`). Правка геометрии
                    // после этого видна тем же предупреждением.
                    this.boundConfig.GeometryFingerprint =
                        ResponseMatrix.GeometryFingerprint(this.boundConfig.Geometry);
                    this.boundConfig.Note = new CDATA(this.lastResult.ImportNote ?? "");
                }
                else
                {
                    // (`AMBER185`) ...и отпечаток геометрии, ДЛЯ КОТОРОЙ она
                    // посчитана, — снятый в начале счёта, а не с полей сейчас.
                    this.boundConfig.GeometryFingerprint = this.lastResult.GeometryFingerprint ?? "";
                }
            }

            // (`AMBER185`) Геометрию правили после счёта: сохраняется и она, и
            // кривая (решение Amber «Отпечаток + предупреждение»), но молчать об
            // этом нельзя — вкладка эффективности и окно отчёта FSA скажут то же.
            if (this.boundConfig.CurveGeometryMismatch)
            {
                AppendLog(Resources.EfficiencyCurveOtherGeometry);
            }

            this.boundConfig.LastUpdated = DateTime.Now;
            this.referenceCurve = this.boundConfig.HasCurve ? this.boundConfig.Curve : null;
            this.dirty = false;
            this.savedIntoBound = true;
            this.UpdateTitle();
            this.UpdateSaveState();
            if (this.SavedIntoConfig != null)
            {
                this.SavedIntoConfig(this, EventArgs.Empty);
            }
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
            // точек: там панель параметров и кнопка расчёта.
            int height;
            if (geometry)
            {
                height = this.saveButton.Top - this.tabControl.Top - 12;
            }
            else
            {
                // По содержимому: кнопка идёт следом за панелью параметров.
                height = this.calculateButton.Bottom + 12 + this.TabChromeHeight;
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

        /// <summary>
        /// Во что обходятся сами корешки вкладок и рамка: разница между высотой
        /// таб-контрола и высотой места под страницу. Считается, а не пишется
        /// числом, — при другом размере шрифта или масштабе экрана она другая.
        /// </summary>
        int TabChromeHeight
        {
            get { return this.tabControl.Height - this.tabControl.DisplayRectangle.Height; }
        }

        void tabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateGeometryLayout();
        }

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
            // Выгружать есть что и без нового счёта: сохранённая кривая
            // конфигурации (задача Amber 07.10.2026: «Хочу экспортировать
            // готовую кривую для анализа. Но без нового расчета кнопка не
            // активна»).
            this.exportButton.Enabled = haveCurve
                || (this.boundConfig != null && this.boundConfig.HasCurve);
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
                : string.Format(CultureInfo.InvariantCulture, "{0} - {1}",
                                this.boundDevice == null ? "" : this.boundDevice.Name,
                                this.boundConfig.Name);
            if (this.dirty)
            {
                title = Resources.EfficiencyMakerDirtyMark + title;
            }

            this.Text = title;
        }

        /// <summary>
        /// Посчитать кривую из геометрии, лежащей в полях редактора. Спектры
        /// для этого не нужны вовсе. Повторное нажатие во время счёта — стоп.
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
            EfficiencyCalculationOptions options = CurrentCalcOptions();
            Start(Resources.EfficiencyMakerCalculating,
                  (log, cancelled) => EfficiencyCalculation.Run(model, options, log, cancelled));
        }

        bool Busy()
        {
            return this.worker != null && this.worker.IsBusy;
        }

        /// <summary>
        /// Обвязка прогона: кнопка запуска становится «Стоп», счёт идёт в фоне,
        /// отмена опрашивается заданием. До 13.09.2026 обвязка была общей на два
        /// прогона (фит по спектрам и расчёт из геометрии); фит снят (`AMBER25`),
        /// кнопка осталась одна.
        /// </summary>
        void Start(string status, Func<Action<string>, Func<bool>, EfficiencyFitResult> job)
        {
            // Журнал НЕ чистится — ни здесь, ни где-либо ещё, пока окно не
            // закрыли: предупреждения разбора геометрии попадают в него при
            // импорте, задолго до прогона, и очистка перед расчётом стирала бы
            // то, к чему расчёт не относится.
            if (this.logTextBox.TextLength > 0)
            {
                AppendLog("");
            }

            this.cancelRequested = false;
            Button trigger = this.calculateButton;
            string caption = trigger.Text;
            trigger.Text = Resources.EfficiencyMakerStop;
            // Параметры запираются на прогон: они сняты в начале счёта, и
            // правка в полях по ходу дела относилась бы уже к следующему разу
            // — а выглядела бы как относящаяся к этому.
            this.calcOptionsGroup.Enabled = false;
            this.saveButton.Enabled = false;
            this.exportButton.Enabled = false;
            this.progressBar.Visible = true;
            this.statusLabel.Text = status;

            // Язык интерфейса выставлен только на потоке формы (MainForm), а
            // счёт идёт на потоке BackgroundWorker: без переноса культуры все
            // строки прогона — сводка разброса, предупреждения, ошибки — брались
            // бы из нейтрального ресурса вместо выбранного языка.
            //
            // ⚠ (`A244`) Культура СЧЁТА переносится уже НЕ ради чисел: расчёт
            // печатает их инвариантом сам, и подмена разделителя в `MainForm`
            // ему не нужна. Перенос оставлен до снятия самого костыля
            // `MainForm`, последней полосой `A244`.
            CultureInfo ui = CultureInfo.CurrentUICulture;
            CultureInfo formatting = CultureInfo.CurrentCulture;

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
                this.calcOptionsGroup.Enabled = true;
                if (args.Error != null)
                {
                    this.statusLabel.Text = args.Error.Message;
                    AppendLog(args.Error.ToString());
                    // Кнопки сохранения гасились на время прогона; ошибка
                    // счёта не повод оставить несохранённую правку геометрии
                    // без кнопки «Сохранить».
                    this.UpdateSaveState();
                    return;
                }

                Finish((EfficiencyFitResult)args.Result);
            };
            this.worker.RunWorkerAsync();
        }

        void Finish(EfficiencyFitResult result)
        {
            // ⛔ (`AMBER185`) ПОСЧИТАННАЯ КРИВАЯ МЕНЯЕТСЯ ТОЛЬКО УДАЧНЫМ СЧЁТОМ.
            // Прежде `lastResult` присваивался ДО проверки ошибки: повторный
            // счёт, прерванный «Стоп» (или упавший), стирал прежнюю
            // несохранённую кривую — часы монте-карло пропадали молча, а
            // «Сохранить» дальше писало одну геометрию.
            if (result == null || !result.Ok)
            {
                string error = result == null || string.IsNullOrEmpty(result.Error)
                    ? Resources.EfficiencyMakerGeometryNoCurve
                    : result.Error;
                this.statusLabel.Text = error;
                AppendLog(error);
                bool kept = this.lastResult != null && this.lastResult.Ok;
                if (kept)
                {
                    AppendLog(Resources.EfficiencyMakerPreviousCurveKept);
                }

                this.graph.SetData(this.referenceCurve, kept ? this.lastResult : null);
                // Кнопки сохранения гасились на время прогона — вернуть их
                // по фактическому состоянию, иначе правка геометрии остаётся
                // без «Сохранить» до первого удачного счёта.
                this.UpdateSaveState();
                return;
            }

            this.lastResult = result;
            this.graph.SetData(this.referenceCurve, result);
            // Пересчитанная кривая — тоже правка: она ещё нигде не сохранена.
            if (result.Ok)
            {
                this.SetDirty();
            }

            this.UpdateSaveState();

            // Итог расчёта из геометрии: сколько точек, в каком диапазоне и
            // откуда уровень (он всегда абсолютный, из геометрии).
            this.statusLabel.Text = string.Format(CultureInfo.InvariantCulture,
                Resources.EfficiencyMakerCalcStatus, result.Curve.Count,
                (int)result.MinEnergy, (int)result.MaxEnergy,
                Resources.EfficiencyMakerLevelSimulation);

            AppendLog("");
            AppendLog(this.statusLabel.Text);
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
                AppendLog(string.Format(CultureInfo.InvariantCulture, Resources.EfficiencyMakerSavedToConfig,
                                        this.boundConfig.Name));
            }
        }

        /// <summary>
        /// Выгрузить кривую в CSV. Свежий расчёт этого окна идёт первым — это
        /// то, что человек только что посчитал и видит сплошной линией; без
        /// него выгружается сохранённая кривая конфигурации (пунктир графика).
        /// Какая из двух ушла в файл, говорит строка `source` шапки.
        /// </summary>
        void exportButton_Click(object sender, EventArgs e)
        {
            bool fresh = this.lastResult != null && this.lastResult.Ok;
            bool saved = !fresh && this.boundConfig != null && this.boundConfig.HasCurve;
            if (!fresh && !saved)
            {
                return;
            }

            List<ROIEfficiencyData> curve = fresh ? this.lastResult.Curve : this.boundConfig.Curve;

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = Resources.EfficiencyMakerCsvFilter;
                dialog.FileName = ExportFileName(this.boundConfig == null ? null : this.boundConfig.Name);
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    // Шапка — после диалога: время `exported` — время записи.
                    EfficiencyCurveIo.ExportCsv(dialog.FileName, curve, this.ExportHeader(fresh, curve));
                    this.statusLabel.Text = string.Format(CultureInfo.InvariantCulture,
                                                          Resources.EfficiencyMakerSaved, dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>
        /// Шапка CSV: прибор, конфигурация, какая кривая (свежий расчёт или
        /// сохранённая), клеймо счёта, даты. Ключи английские, как и столбцы
        /// таблицы, — это формат файла, а не текст окна; даты и числа —
        /// инвариантом.
        /// </summary>
        List<string> ExportHeader(bool fresh, List<ROIEfficiencyData> curve)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            List<string> header = new List<string> { "BecqMoni efficiency curve" };
            if (this.boundDevice != null)
            {
                header.Add("device: " + this.boundDevice.Name);
            }

            if (this.boundConfig != null)
            {
                header.Add("configuration: " + this.boundConfig.Name);
            }

            if (fresh)
            {
                // (П256, 14.10.2026) «Сохранён ли» — по СОДЕРЖИМОМУ конфигурации, а
                // не одной строкой на все случаи: свежий расчёт остаётся в окне и
                // после «Сохранить», и шапка выгрузки уверяла «NOT saved» о кривой,
                // которая уже лежит в конфигурации точка в точку.
                string made = this.lastResult.Origin == EfficiencyOrigin.Lsrm
                    ? "source: imported from an LSRM file in the curve designer"
                    : "source: computed in the curve designer";
                header.Add(made + (this.LastResultIsInConfig()
                    ? ", saved into the configuration"
                    : ", NOT saved into the configuration"));
                header.Add("origin: " + this.lastResult.Origin);
                header.Add("stamp: " + (this.lastResult.ComputeStamp ?? ""));
                if (!string.IsNullOrEmpty(this.lastResult.ImportNote))
                {
                    header.Add("import: " + this.lastResult.ImportNote);
                }
            }
            else
            {
                header.Add("source: saved curve of the configuration");
                header.Add("origin: " + this.boundConfig.Origin);
                header.Add("stamp: " + (this.boundConfig.ComputeStamp ?? ""));
                header.Add("saved: " + this.boundConfig.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss", inv));
                if (this.boundConfig.CurveGeometryMismatch)
                {
                    header.Add("warning: the geometry of the configuration was edited after this curve was computed");
                }
            }

            double eMin = double.MaxValue, eMax = double.MinValue;
            foreach (ROIEfficiencyData point in curve)
            {
                eMin = Math.Min(eMin, point.Energy);
                eMax = Math.Max(eMax, point.Energy);
            }

            header.Add(curve.Count > 0
                ? string.Format(inv, "points: {0}, {1:G6}...{2:G6} keV", curve.Count, eMin, eMax)
                : "points: 0");
            header.Add("exported: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv));
            header.Add("columns: E_keV = energy, keV; eps = full-energy peak efficiency; err_pct = relative error, %");
            return header;
        }

        /// <summary>
        /// (П256) Лежит ли свежий расчёт окна в привязанной конфигурации ТЕМИ ЖЕ
        /// точками и тем же клеймом — то есть сохранён ли он «Сохранить» и не
        /// отброшен с клоном (<see cref="Rebind"/>). Сравнение по содержимому, а
        /// не по признакам окна: признаки живут каждый своей жизнью (`dirty`
        /// взводит и правка геометрии, `savedIntoBound` снимает перепривязка), а
        /// вопрос шапки — о кривой.
        /// </summary>
        bool LastResultIsInConfig()
        {
            if (this.lastResult == null || !this.lastResult.Ok || this.boundConfig == null
                || !this.boundConfig.HasCurve || this.lastResult.Curve == null)
            {
                return false;
            }

            List<ROIEfficiencyData> mine = this.lastResult.Curve;
            List<ROIEfficiencyData> saved = this.boundConfig.Curve;
            if (mine.Count != saved.Count
                || !string.Equals(this.lastResult.ComputeStamp ?? "", this.boundConfig.ComputeStamp ?? "", StringComparison.Ordinal))
            {
                return false;
            }

            for (int i = 0; i < mine.Count; i++)
            {
                if (mine[i] == null || saved[i] == null
                    || !mine[i].Energy.Equals(saved[i].Energy)
                    || !mine[i].Efficiency.Equals(saved[i].Efficiency)
                    || !mine[i].ErrorPercent.Equals(saved[i].ErrorPercent))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Имя файла по умолчанию — имя конфигурации без запрещённых знаков.</summary>
        static string ExportFileName(string configName)
        {
            if (string.IsNullOrWhiteSpace(configName))
            {
                return "efficiency.csv";
            }

            char[] bad = System.IO.Path.GetInvalidFileNameChars();
            System.Text.StringBuilder name = new System.Text.StringBuilder();
            foreach (char c in configName.Trim())
            {
                name.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            }

            return name + ".csv";
        }

        /// <summary>
        /// Отмена проверяется между узлами кривой, и расчёт может идти ещё
        /// долго после закрытия окна. Держать окно до конца нельзя, поэтому
        /// оно закрывается сразу, счёт получает сигнал отмены, а запоздавшие
        /// обработчики гасятся проверкой IsDisposed.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // (`AMBER201`, Р4, подозрение полосы 8) Крестик при несохранённой
            // работе — вопрос. Прежде окно закрывалось молча, и посчитанная, но
            // не положенная в конфигурацию кривая (минуты и часы счёта) или
            // правка геометрии пропадали. Не спрашивается, когда закрытие решено
            // снаружи (<see cref="CloseWithoutAsking"/>: вопрос о сохранении
            // конфигурации прибора, удаление кривой), при закрытии владельца и
            // без окон.
            if (!this.closingDecided && e.CloseReason == CloseReason.UserClosing
                && (this.dirty || Busy()) && AppUi.HasWindows)
            {
                DialogResult answer = MessageBox.Show(this,
                    ResourceText("EfficiencyMakerCloseUnsaved",
                        "The curve designer holds work that is not saved into the device configuration"
                        + " (geometry edits, a computed curve or a running calculation)."
                        + Environment.NewLine + Environment.NewLine + "Close it and discard this work?"),
                    this.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            this.cancelRequested = true;
            base.OnFormClosing(e);
        }

        bool closingDecided;

        /// <summary>
        /// (`AMBER201`) Закрыть без вопроса о несохранённом — решение о нём уже
        /// принято там, откуда зовут (форма прибора).
        /// </summary>
        public void CloseWithoutAsking()
        {
            this.closingDecided = true;
            this.Close();
        }

        /// <summary>Строка ресурсов с запасным текстом (ключ заведён без перегенерации Designer).</summary>
        static string ResourceText(string key, string fallback)
        {
            string text = Resources.ResourceManager.GetString(key, Resources.Culture);
            return string.IsNullOrEmpty(text) ? fallback : text;
        }

        void AppendLog(string message)
        {
            // Строка добавляется, а не переписывается целиком: журнал расчёта
            // (узлы кривой, разбор геометрии) идёт на сотни строк, и
            // присваивание Text на каждой из них перестраивало весь текст
            // заново — окно вставало.
            if (this.IsDisposed || this.Disposing)
            {
                return;
            }

            this.logTextBox.AppendText((message ?? "") + Environment.NewLine);
        }
    }
}
