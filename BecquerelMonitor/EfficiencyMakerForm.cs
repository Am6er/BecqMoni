using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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

        // ------------------------------------------------------------------
        // Параметры расчёта из геометрии
        // ------------------------------------------------------------------

        GroupBox calcOptionsGroup;

        NumericUpDown calcMinEnergyBox, calcMaxEnergyBox, calcPointsBox,
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
            // Верх — от НИЖНЕГО КРАЯ подсказки, а не числом: подсказка
            // авторазмерная, и по-русски она на строку длиннее, чем
            // по-английски. Прибитая координата наехала бы на неё в одном из
            // двух языков.
            this.calcOptionsGroup = new GroupBox
            {
                Text = Resources.ResponseMatrixParameters,
                Location = new System.Drawing.Point(13, this.calcHintLabel.Bottom + 10),
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
            this.calcHistoriesBox.ThousandsSeparator = true;

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

        NumericUpDown CalcField(string caption, int x, int y,
                                decimal min, decimal max, decimal value)
        {
            this.calcOptionsGroup.Controls.Add(new Label
            {
                Text = caption,
                Location = new System.Drawing.Point(x, y + 3),
                Size = new System.Drawing.Size(CalcLabelWidth, 18),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            });

            NumericUpDown box = new NumericUpDown
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

        static void SetClamped(NumericUpDown box, decimal value)
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

                return string.Format(CultureInfo.CurrentCulture,
                                     Resources.EfficiencyMakerCalcRestored, config.ComputeStamp);
            }

            if (!CurveRange(config, out lo, out hi))
            {
                return "";
            }

            SetClamped(this.calcMinEnergyBox, (decimal)lo);
            SetClamped(this.calcMaxEnergyBox, (decimal)hi);
            return string.Format(CultureInfo.CurrentCulture,
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

            // Геометрии может не быть — так открывается и новая конфигурация, и
            // кривая, восстановленная по измерениям. Тогда в поля заезжает
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

            // Кривая конфигурации становится исходной: по ней подгонка получает
            // уровень, и по ней же график рисует полосу отличий.
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
                this.boundConfig.Origin = this.lastResult.LevelSource == EfficiencyLevelSource.Simulation
                    ? EfficiencyOrigin.Simulation
                    : EfficiencyOrigin.Measurement;
                // Клеймо едет вместе с кривой (E12): у измерительной кривой
                // оно пустое, и это тоже правда — физики переноса в ней нет.
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
            // точек: там всего подсказка и одна кнопка.
            int height;
            if (geometry)
            {
                height = this.saveButton.Top - this.tabControl.Top - 12;
            }
            else if (this.tabControl.SelectedTab == this.tabPageCalculate)
            {
                // По СОДЕРЖИМОМУ, а не числом: панель параметров стоит под
                // подсказкой, подсказка авторазмерная и по-русски на строку
                // длиннее, а кнопка съезжает вслед за панелью. Прежняя
                // константа была снята с прежнего содержимого — подсказки и
                // одной кнопки — и панель параметров она обрезала.
                height = this.calculateButton.Bottom + 12 + this.TabChromeHeight;
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

        /// <summary>
        /// Во что обходятся сами корешки вкладок и рамка: разница между высотой
        /// таб-контрола и высотой места под страницу. Считается, а не пишется
        /// числом, — при другом размере шрифта или масштабе экрана она другая.
        /// </summary>
        int TabChromeHeight
        {
            get { return this.tabControl.Height - this.tabControl.DisplayRectangle.Height; }
        }

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
        /// <summary>
        /// (E6) Чем спектры пачки различаются по СЪЁМКЕ. Пусто — расхождений не
        /// нашлось (или сравнивать не по чему, и это тоже сказано).
        ///
        /// Сравнивается то, что в самом файле спектра и что задаёт геометрию:
        /// прибор (разный прибор — разная съёмка заведомо), геометрия
        /// прикреплённой кривой, масса и объём пробы. Ни одно из них не
        /// «геометрия» целиком, но расхождение любого означает, что пачку
        /// усредняют зря. Отказом это НЕ делается: бывает и осознанная сборная
        /// пачка, а вот молчать нельзя.
        ///
        /// Файлы читаются облегчённо — без разбора калибровок и без обращения к
        /// конфигурациям устройств: те же файлы всё равно прочтёт фиттер, а
        /// упасть здесь на спектре, который он прочитал бы, было бы хуже
        /// молчания. Любая беда чтения — строка в журнале, и только.
        /// </summary>
        List<string> PackGeometryComplaints()
        {
            var devices = new Dictionary<string, List<string>>();
            var geometries = new Dictionary<string, List<string>>();
            var amounts = new Dictionary<string, List<string>>();
            var complaints = new List<string>();

            foreach (string path in this.spectrumFiles)
            {
                string shown = Path.GetFileNameWithoutExtension(path);
                ResultData data;
                try
                {
                    var serializer = new System.Xml.Serialization.XmlSerializer(typeof(ResultDataFile));
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var file = (ResultDataFile)serializer.Deserialize(stream);
                        data = file.ResultDataList != null && file.ResultDataList.Count > 0
                            ? file.ResultDataList[0]
                            : null;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                if (data == null)
                {
                    continue;
                }

                Add(devices, data.DeviceConfigReference == null
                    ? "" : data.DeviceConfigReference.Name ?? "", shown);

                EfficiencyConfigData eff = data.Efficiency ?? data.FileEfficiency;
                Add(geometries, eff != null && eff.HasGeometry ? eff.Geometry.Describe() : "", shown);

                Add(amounts, data.SampleInfo == null ? "" : string.Format(
                    CultureInfo.CurrentCulture, "{0:0.###} г / {1:0.###} мл",
                    data.SampleInfo.Weight, data.SampleInfo.Volume), shown);
            }

            Complain(complaints, devices, Resources.EfficiencyMakerPackDevices);
            Complain(complaints, geometries, Resources.EfficiencyMakerPackGeometries);
            Complain(complaints, amounts, Resources.EfficiencyMakerPackAmounts);
            return complaints;
        }

        static void Add(Dictionary<string, List<string>> map, string key, string spectrum)
        {
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            List<string> list;
            if (!map.TryGetValue(key, out list))
            {
                list = new List<string>();
                map[key] = list;
            }

            list.Add(spectrum);
        }

        static void Complain(List<string> complaints, Dictionary<string, List<string>> map, string caption)
        {
            if (map.Count < 2)
            {
                return;
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, List<string>> pair in map)
            {
                parts.Add(string.Format(CultureInfo.CurrentCulture, "{0} ({1})",
                                        pair.Key, string.Join(", ", pair.Value.ToArray())));
            }

            complaints.Add(string.Format(CultureInfo.CurrentCulture, caption,
                                         map.Count, string.Join("; ", parts.ToArray())));
        }

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
            EfficiencyCalculationOptions options = CurrentCalcOptions();
            Start(this.calculateButton, this.runButton, Resources.EfficiencyMakerCalculating,
                  (log, cancelled) => EfficiencyCalculation.Run(
                      model, options, log, cancelled));
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
            // Параметры запираются на любой прогон, а не только на свой: они
            // сняты в начале счёта, и правка в полях по ходу дела относилась бы
            // уже к следующему разу — а выглядела бы как относящаяся к этому.
            this.calcOptionsGroup.Enabled = false;
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
                // Обе кнопки запуска доступны всегда: в редакторе всегда лежит
                // геометрия (см. конструктор). Прежний возврат «как было»
                // гасил расчёт навсегда, если первым прошёл фит, — this.geometry
                // до первого импорта пуст, хотя считать есть по чему.
                other.Enabled = true;
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

            // (E6) Пачка обязана быть ОДНОЙ геометрии: эффективность зависит от
            // телесного угла и самопоглощения в пробе, и спектры разных съёмок
            // дают бессмысленную среднюю кривую. Прежде форма писала об этом
            // только в заголовке списка — предупреждением ВООБЩЕ, которое нечем
            // соотнести с тем, что человек сейчас положил.
            foreach (string line in this.PackGeometryComplaints())
            {
                AppendLog(line);
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

            if (!this.AskFallbackDevice(input))
            {
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

        /// <summary>
        /// Спектр без своей калибровки ПШПВ берёт её у конфигурации прибора, на
        /// которую ссылается. Ссылка может никуда не вести: так бывает у файлов,
        /// переживших переименование прибора. Прежде такой спектр просто
        /// выпадал из прогона с сообщением, и сделать с этим было нечего —
        /// подставить конфигурацию было нечем.
        ///
        /// Теперь спрашиваем. Именно спрашиваем, а не подставляем: от
        /// конфигурации зависят обе калибровки, и молча взятая чужая даёт
        /// правдоподобные и неверные площади.
        ///
        /// false — от выбора отказались, прогон не начинаем.
        /// </summary>
        bool AskFallbackDevice(EfficiencyFitInput input)
        {
            List<DeviceConfigInfo> devices = DeviceConfigManager.GetInstance().DeviceConfigList;
            HashSet<string> missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            foreach (string path in input.SpectrumFiles)
            {
                string device;
                if (Utils.SpectrumScout.NeedsDeviceConfig(path, out device)
                    && !devices.Exists(d => string.Equals(d.Guid, device, StringComparison.OrdinalIgnoreCase)))
                {
                    count++;
                    missing.Add(device);
                }
            }

            if (count == 0)
            {
                return true;
            }

            string[] names = new string[missing.Count];
            missing.CopyTo(names);
            DeviceConfigInfo chosen = (DeviceConfigInfo)PickOneForm.Ask(this,
                Resources.EfficiencyMakerDeviceGoneTitle,
                string.Format(CultureInfo.CurrentCulture, Resources.EfficiencyMakerDeviceGoneQuestion,
                              count, string.Join(", ", names)),
                devices.ConvertAll<object>(d => d), null);
            if (chosen == null)
            {
                return false;
            }

            input.FallbackDeviceGuid = chosen.Guid;
            AppendLog(string.Format(CultureInfo.CurrentCulture,
                                    Resources.EfficiencyMakerDeviceGoneQuestion, count,
                                    string.Join(", ", names)) + " " + chosen.Name);
            return true;
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
