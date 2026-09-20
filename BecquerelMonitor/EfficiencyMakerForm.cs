using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
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
    /// первой вкладке (или ввозится из файла LSRM `.in`), расчёт запускается со
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
            this.geometryPanel.SetSceneEnergy((double)this.calcMaxEnergyBox.Value);

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
                SetClamped(this.calcMinEnergyBox, (decimal)lo);
                SetClamped(this.calcMaxEnergyBox, (decimal)hi);
                if (histories > 0.0)
                {
                    SetClamped(this.calcHistoriesBox, (decimal)histories);
                }

                this.calcGridBox.SelectedIndex = logGrid ? 1 : 0;
                if (logGrid && nodes > 0.0)
                {
                    SetClamped(this.calcPointsBox, (decimal)nodes);
                }

                return string.Format(CultureInfo.InvariantCulture,
                                     Resources.EfficiencyMakerCalcRestored, config.ComputeStamp);
            }

            if (!CurveRange(config, out lo, out hi))
            {
                return "";
            }

            SetClamped(this.calcMinEnergyBox, (decimal)lo);
            SetClamped(this.calcMaxEnergyBox, (decimal)hi);
            return string.Format(CultureInfo.InvariantCulture,
                                 Resources.EfficiencyMakerRangeFromCurve, lo, hi);
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
            // старая кривая, у которой геометрии нет (ввезена или восстановлена
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
            string restored = this.ApplyCalcOptions(config);
            if (!string.IsNullOrEmpty(restored))
            {
                AppendLog(restored);
            }
            this.dirty = false;
            this.UpdateTitle();
            this.UpdateSaveState();
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
                // Кривую здесь даёт только расчёт из геометрии (`AMBER25`,
                // 13.09.2026): прежняя развилка по `LevelSource` на
                // `EfficiencyOrigin.Measurement` ушла вместе с фитом.
                this.boundConfig.Origin = EfficiencyOrigin.Simulation;
                // Клеймо едет вместе с кривой (E12).
                this.boundConfig.ComputeStamp = this.lastResult.ComputeStamp ?? "";
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
            this.lastResult = result;
            if (!string.IsNullOrEmpty(result.Error))
            {
                this.statusLabel.Text = result.Error;
                AppendLog(result.Error);
                this.graph.SetData(this.referenceCurve, null);
                // Кнопки сохранения гасились на время прогона — вернуть их
                // по фактическому состоянию, иначе правка геометрии остаётся
                // без «Сохранить» до первого удачного счёта.
                this.UpdateSaveState();
                return;
            }

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
                    EfficiencyCurveIo.ExportCsv(dialog.FileName, this.lastResult);
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
        /// Отмена проверяется между узлами кривой, и расчёт может идти ещё
        /// долго после закрытия окна. Держать окно до конца нельзя, поэтому
        /// оно закрывается сразу, счёт получает сигнал отмены, а запоздавшие
        /// обработчики гасятся проверкой IsDisposed.
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            this.cancelRequested = true;
            base.OnFormClosing(e);
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
