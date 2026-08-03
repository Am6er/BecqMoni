using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
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
        List<ROIEfficiencyData> referenceCurve;
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
            LoadChains();
        }

        void LoadChains()
        {
            this.chainsCheckedListBox.Items.Clear();
            Dictionary<string, List<EfficiencyLine>> chains = EfficiencyLibrary.BuildChains();
            foreach (string name in chains.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                // Ничего не отмечается по умолчанию. Метод стоит на том, что у
                // всех линий набора ОДНА активность: это верно для цепочки в
                // вековом равновесии и неверно для сборного набора вроде
                // «NORM» (торий, уран и калий разом). Отличить их по составу
                // нечем, а молча взятый сборный набор портит кривую — на
                // пачке ASN16 он давал χ²/ndf 173 против 127.
                this.chainsCheckedListBox.Items.Add(name, false);
            }

            if (this.chainsCheckedListBox.Items.Count == 0)
            {
                AppendLog(Resources.EfficiencyMakerNoChains);
            }
        }

        // ------------------------------------------------------------------
        // Ввод
        // ------------------------------------------------------------------

        void referenceBrowseButton_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = Resources.EfficiencyMakerRoiFilter;
                dialog.InitialDirectory = RoiConfigDirectory();
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    this.referenceCurve = EfficiencyFitter.LoadReferenceCurve(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                this.referenceTextBox.Text = dialog.FileName;
                AppendLog(string.Format(Resources.EfficiencyMakerReferenceLoaded,
                    Path.GetFileName(dialog.FileName), this.referenceCurve.Count));
                if (string.IsNullOrEmpty(this.outputTextBox.Text))
                {
                    this.outputTextBox.Text = Path.Combine(
                        Path.GetDirectoryName(dialog.FileName),
                        Path.GetFileNameWithoutExtension(dialog.FileName) + " (fitted).xml");
                }

                this.graph.SetData(this.referenceCurve, this.lastResult);
            }
        }

        void referenceClearButton_Click(object sender, EventArgs e)
        {
            this.referenceCurve = null;
            this.referenceTextBox.Text = "";
            this.graph.SetData(null, this.lastResult);
        }

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
                    if (!this.spectrumFiles.Contains(file, StringComparer.OrdinalIgnoreCase))
                    {
                        this.spectrumFiles.Add(file);
                        this.spectraListBox.Items.Add(file);
                    }
                }
            }
        }

        void spectraRemoveButton_Click(object sender, EventArgs e)
        {
            foreach (int index in this.spectraListBox.SelectedIndices.Cast<int>()
                     .OrderByDescending(i => i).ToList())
            {
                this.spectrumFiles.RemoveAt(index);
                this.spectraListBox.Items.RemoveAt(index);
            }
        }

        void spectraClearButton_Click(object sender, EventArgs e)
        {
            this.spectrumFiles.Clear();
            this.spectraListBox.Items.Clear();
        }

        void outputBrowseButton_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = Resources.EfficiencyMakerRoiFilter;
                dialog.FileName = this.outputTextBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    this.outputTextBox.Text = dialog.FileName;
                }
            }
        }

        void geometryBrowseButton_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = Resources.EfficiencyMakerGeometryFilter;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                GeometryModel model;
                try
                {
                    model = GeometryModel.Load(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                this.geometry = model;
                this.geometryTextBox.Text = dialog.FileName;
                this.calculateButton.Enabled = true;
                AppendLog(string.Format(Resources.EfficiencyMakerGeometryLoaded, model.Describe()));
                if (string.IsNullOrEmpty(this.outputTextBox.Text))
                {
                    this.outputTextBox.Text = Path.Combine(
                        Path.GetDirectoryName(dialog.FileName),
                        Path.GetFileNameWithoutExtension(dialog.FileName) + " (calculated).xml");
                }
            }
        }

        void geometryClearButton_Click(object sender, EventArgs e)
        {
            this.geometry = null;
            this.geometryTextBox.Text = "";
            this.calculateButton.Enabled = false;
        }

        static string RoiConfigDirectory()
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData), "BecqMoni", "config", "ROI");
                return Directory.Exists(dir) ? dir : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        // ------------------------------------------------------------------
        // Счёт
        // ------------------------------------------------------------------

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
            this.logTextBox.Clear();
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
            EfficiencyFitInput input = new EfficiencyFitInput();
            input.SpectrumFiles.AddRange(this.spectrumFiles);
            foreach (object item in this.chainsCheckedListBox.CheckedItems)
            {
                input.Chains.Add(item.ToString());
            }

            if (input.Chains.Count == 0)
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
            input.ReferencePath = this.referenceTextBox.Text;

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
            this.saveButton.Enabled = result.Ok && this.outputTextBox.Text.Trim().Length > 0;
            this.exportButton.Enabled = result.Ok;

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

        void saveButton_Click(object sender, EventArgs e)
        {
            if (this.lastResult == null || !this.lastResult.Ok)
            {
                return;
            }

            string path = this.outputTextBox.Text.Trim();
            if (path.Length == 0)
            {
                outputBrowseButton_Click(sender, e);
                path = this.outputTextBox.Text.Trim();
                if (path.Length == 0)
                {
                    return;
                }
            }

            try
            {
                string note = this.lastResult.LevelSource == EfficiencyLevelSource.Simulation
                    ? string.Format(CultureInfo.InvariantCulture,
                        "Efficiency maker: calculated from geometry {0}, {1} points, {2}-{3} keV, "
                        + "{4} histories per point",
                        Path.GetFileName(this.geometryTextBox.Text), this.lastResult.Curve.Count,
                        (int)this.lastResult.MinEnergy, (int)this.lastResult.MaxEnergy,
                        SimulationHistories)
                    : string.Format(CultureInfo.InvariantCulture,
                        "Efficiency maker: {0} lines, {1} series, chi2/ndf {2:F2}, {3}-{4} keV, level: {5}",
                        this.lastResult.AcceptedCount, this.lastResult.SeriesKeys.Count,
                        this.lastResult.Chi2Ndf, (int)this.lastResult.MinEnergy,
                        (int)this.lastResult.MaxEnergy, this.lastResult.LevelSource);
                EfficiencyFitter.SaveCurve(path, this.referenceTextBox.Text,
                    Path.GetFileNameWithoutExtension(path), this.lastResult.Curve, note);
                this.statusLabel.Text = string.Format(Resources.EfficiencyMakerSaved, path);
                AppendLog(this.statusLabel.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
