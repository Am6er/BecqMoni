using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;
using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    /// <summary>
    /// Создание и пересоздание матрицы отклика для геометрии выбранной кривой
    /// эффективности.
    ///
    /// Что делает форма при открытии: ищет матрицу этой геометрии, проверяет её
    /// годность по отпечатку и говорит одно из трёх — нет, устарела, годна. У
    /// годной показывает подробности. Проверка нужна именно по отпечатку, а не
    /// по наличию файла: геометрию могли поправить после расчёта, и посчитать
    /// спектр по матрице чужой геометрии хуже, чем не посчитать вовсе.
    ///
    /// Счёт идёт в фоне, с прогрессом, оценкой остатка и отменой: даже минута
    /// без признаков жизни выглядит как зависшая программа. Оценка остатка
    /// берётся от времени УЖЕ посчитанных узлов, а не от их доли: узлы наверху
    /// шкалы дороже нижних, и пропорция «сделано к общему» врала бы.
    ///
    /// Сохранение — отдельной кнопкой и только по нажатию: посчитанная матрица
    /// живёт в памяти формы, пока человек не решил, что она ему нужна.
    /// </summary>
    public partial class ResponseMatrixForm : Form
    {
        readonly EfficiencyConfigData config;

        Label stateLabel, versionsLabel, estimateLabel, progressLabel;
        CheckBox useMatrixCheck;
        Panel detailsPanel;
        string detailsText = "";
        ProgressBar progressBar;
        Button computeButton, cancelButton, saveButton, closeButton;
        NumericUpDown minEnergyBox, maxEnergyBox, nodesBox, binBox, historiesBox, threadsBox;

        /// <summary>
        /// С какой ошибки интеграла континуума строки предупреждать, %.
        /// На контактной геометрии аналоговая ветка набирает десятки тысяч
        /// событий на узел (доли процента), так что порог молчит там, где
        /// всё хорошо, и говорит на дальней геометрии и точечном источнике.
        /// </summary>
        const double ContinuumNoiseWarnPercent = 2.0;

        CancellationTokenSource cancellation;
        ResponseMatrix computed;
        bool busy;

        // Номер ПОСЛЕДНЕГО запроса оценки времени. Загрузка формы дёргает
        // ValueChanged у пяти полей подряд, и без номера ярлык доставался
        // последней ФИНИШИРОВАВШЕЙ задаче, а не последней запрошенной.
        int estimateRequest;

        /// <summary>
        /// Трогали ли выключатель матрицы (W11). Форма пишет в ту же копию
        /// конфигурации, что и вкладка Efficiency, — вкладке остаётся пометить
        /// конфигурацию изменённой, чтобы «Сохранить» ожило.
        /// </summary>
        public bool UseMatrixTouched { get; private set; }

        void UseMatrixChanged(object sender, EventArgs e)
        {
            if (this.config != null && this.config.UseResponseMatrix != this.useMatrixCheck.Checked)
            {
                this.config.UseResponseMatrix = this.useMatrixCheck.Checked;
                this.UseMatrixTouched = true;
            }
        }

        public ResponseMatrixForm(EfficiencyConfigData config)
        {
            this.config = config;
            this.BuildLayout();
            this.LoadExisting();
            this.UpdateEstimateAsync();
        }

        ResponseMatrixOptions CurrentOptions()
        {
            return new ResponseMatrixOptions
            {
                MinEnergyKev = (double)this.minEnergyBox.Value,
                MaxEnergyKev = (double)this.maxEnergyBox.Value,
                NodeCount = (int)this.nodesBox.Value,
                BinKev = (double)this.binBox.Value,
                Histories = (int)this.historiesBox.Value,
                Threads = (int)this.threadsBox.Value
            };
        }

        // ------------------------------------------------------------------
        // Состояние
        // ------------------------------------------------------------------

        void LoadExisting()
        {
            if (this.config == null || !this.config.HasGeometry)
            {
                this.stateLabel.Text = Resources.ResponseMatrixNoGeometry;
                this.ShowVersions(0, 0, false);
                this.SetDetails("");
                this.computeButton.Enabled = false;
                // Без геометрии матрицы не бывает — выключателю нечего включать.
                this.useMatrixCheck.Enabled = false;
                return;
            }

            ResponseMatrix existing = ResponseMatrixStore.Load(this.config.Guid);
            if (existing == null)
            {
                // Файл может лежать, но быть другого поколения — Load для него
                // молча возвращает null, и без заглядывания в заголовок форма
                // говорила бы «не посчитана» про матрицу, которая посчитана,
                // просто устарела. Различие пользователю важно.
                int fileFormat, filePhysics;
                if (ResponseMatrix.PeekVersions(ResponseMatrixStore.PathOf(this.config.Guid),
                                                out fileFormat, out filePhysics))
                {
                    this.stateLabel.Text = Resources.ResponseMatrixStateStaleVersions;
                    this.ShowVersions(filePhysics, fileFormat, true);
                    this.computeButton.Text = Resources.ResponseMatrixRecompute;
                }
                else
                {
                    this.stateLabel.Text = Resources.ResponseMatrixStateMissing;
                    this.ShowVersions(0, 0, false);
                    this.computeButton.Text = Resources.ResponseMatrixCompute;
                }

                this.SetDetails("");
                return;
            }

            // Параметры берутся ИЗ САМОЙ матрицы, где они и сохранены:
            // сравнивать её с тем, что сейчас выставлено в полях, значило бы
            // объявлять устаревшей любую матрицу, стоило человеку тронуть
            // ползунок. Восстанавливать их из краёв сетки тоже нельзя —
            // `exp(log(30))` даёт 30.000000000000004, и отпечаток не сходится.
            int physics = ResponseMatrix.PhysicsFromStamp(existing.Stamp);
            bool versionsMatch = physics == ResponseMatrix.PhysicsVersion;
            bool valid = versionsMatch && existing.IsValidFor(this.config.Geometry);
            this.stateLabel.Text = valid
                ? Resources.ResponseMatrixStateValid
                : versionsMatch
                    ? Resources.ResponseMatrixStateStale
                    : Resources.ResponseMatrixStateStaleVersions;
            this.ShowVersions(physics, ResponseMatrix.FormatVersion, !versionsMatch);
            this.SetDetails(this.Describe(existing));
            this.computeButton.Text = Resources.ResponseMatrixRecompute;

            // Поля выставляются по тому, чем матрица посчитана, — чтобы
            // «Пересчитать» повторяло её, а не умолчания формы. С зажимом в
            // границы контролов: `.rmx` с параметрами вне диапазонов UI (чужой
            // или посчитанный другой сборкой) ронял форму
            // ArgumentOutOfRangeException, и матрицу нельзя было даже
            // пересчитать.
            if (existing.Options != null)
            {
                SetClamped(this.minEnergyBox, (decimal)existing.Options.MinEnergyKev);
                SetClamped(this.maxEnergyBox, (decimal)existing.Options.MaxEnergyKev);
                SetClamped(this.nodesBox, existing.Options.NodeCount);
                SetClamped(this.binBox, (decimal)existing.Options.BinKev);
                SetClamped(this.historiesBox, existing.Options.Histories);
            }
        }

        static void SetClamped(NumericUpDown box, decimal value)
        {
            box.Value = Math.Min(box.Maximum, Math.Max(box.Minimum, value));
        }

        /// <summary>
        /// Строка версий генерации. Ноль в версии матрицы — матрицы нет, тогда
        /// печатаются только текущие версии кода; несовпадение подсвечивается,
        /// потому что именно оно браковало матрицу молча.
        /// </summary>
        void ShowVersions(int matrixPhysics, int matrixFormat, bool mismatch)
        {
            this.versionsLabel.Text = matrixPhysics > 0 || matrixFormat > 0
                ? string.Format(CultureInfo.CurrentCulture, Resources.ResponseMatrixVersionsBoth,
                                matrixPhysics, matrixFormat,
                                ResponseMatrix.PhysicsVersion, ResponseMatrix.FormatVersion)
                : string.Format(CultureInfo.CurrentCulture, Resources.ResponseMatrixVersionsCurrent,
                                ResponseMatrix.PhysicsVersion, ResponseMatrix.FormatVersion);
            this.versionsLabel.ForeColor = mismatch ? Color.Firebrick : SystemColors.GrayText;
        }

        string Describe(ResponseMatrix matrix)
        {
            long fileBytes = ResponseMatrixStore.FileSize(this.config.Guid);
            return string.Format(CultureInfo.CurrentCulture, Resources.ResponseMatrixDetails,
                                 matrix.NodeCount,
                                 matrix.Energies[0],
                                 matrix.Energies[matrix.NodeCount - 1],
                                 matrix.BinKev,
                                 matrix.Histories,
                                 matrix.DataBytes / 1024.0,
                                 fileBytes / 1024.0,
                                 matrix.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                                 matrix.BuildSeconds);
        }

        // ------------------------------------------------------------------
        // Оценка времени
        // ------------------------------------------------------------------

        async void UpdateEstimateAsync()
        {
            if (this.config == null || !this.config.HasGeometry || this.busy)
            {
                return;
            }

            int request = ++this.estimateRequest;
            this.estimateLabel.Text = Resources.ResponseMatrixEstimating;
            GeometryModel geometry = this.config.Geometry.Clone();
            ResponseMatrixOptions options = this.CurrentOptions();
            double seconds;
            try
            {
                seconds = await Task.Run(() => ResponseMatrixBuilder.EstimateSeconds(geometry, options));
            }
            catch (Exception)
            {
                if (!this.IsDisposed && request == this.estimateRequest)
                {
                    this.estimateLabel.Text = "";
                }

                return;
            }

            // Пишет только последний запрошенный: устаревшая задача, финишировав
            // позже, перетёрла бы свежую оценку своей.
            if (!this.IsDisposed && request == this.estimateRequest)
            {
                this.estimateLabel.Text = string.Format(CultureInfo.CurrentCulture,
                    Resources.ResponseMatrixEstimate, Duration(seconds));
            }
        }

        static string Duration(double seconds)
        {
            if (seconds < 0.0)
            {
                return "?";
            }

            TimeSpan span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1.0
                ? string.Format(CultureInfo.CurrentCulture, "{0:%h}:{0:mm}:{0:ss}", span)
                : string.Format(CultureInfo.CurrentCulture, "{0:%m}:{0:ss}", span);
        }

        // ------------------------------------------------------------------
        // Счёт
        // ------------------------------------------------------------------

        async void ComputeClick(object sender, EventArgs e)
        {
            if (this.busy || this.config == null || !this.config.HasGeometry)
            {
                return;
            }

            GeometryModel geometry = this.config.Geometry.Clone();
            ResponseMatrixOptions options = this.CurrentOptions();
            this.cancellation = new CancellationTokenSource();
            this.SetBusy(true);
            this.progressBar.Value = 0;
            this.progressBar.Maximum = Math.Max(1, options.NodeCount);

            var progress = new Progress<ResponseMatrixProgress>(this.ShowProgress);
            try
            {
                ResponseMatrix matrix = await Task.Run(
                    () => ResponseMatrixBuilder.Build(geometry, options, progress, this.cancellation.Token),
                    this.cancellation.Token);

                this.computed = matrix;
                this.progressBar.Value = this.progressBar.Maximum;
                this.progressLabel.Text = string.Format(CultureInfo.CurrentCulture,
                    Resources.ResponseMatrixDone, Duration(matrix.BuildSeconds));

                // Континуум набирается аналоговой веткой полной сферой, и на
                // дальней геометрии до кристалла доходит доля телесного угла:
                // пик остаётся точным, а континуум может оказаться шумом. Без
                // этой строки различить нечем — оценка ошибки, что стоит выше,
                // описывает пик (F23).
                if (matrix.ContinuumRelativeError > ContinuumNoiseWarnPercent)
                {
                    this.progressLabel.Text += string.Format(CultureInfo.CurrentCulture,
                        Resources.ResponseMatrixContinuumNoise,
                        matrix.ContinuumRelativeError.ToString("n1", CultureInfo.CurrentCulture));
                }

                this.SetDetails(this.Describe(matrix));
                this.stateLabel.Text = Resources.ResponseMatrixStateValid;
                this.ShowVersions(ResponseMatrix.PhysicsVersion, ResponseMatrix.FormatVersion, false);
            }
            catch (OperationCanceledException)
            {
                this.computed = null;
                this.progressBar.Value = 0;
                this.progressLabel.Text = Resources.ResponseMatrixCancelled;
            }
            catch (Exception ex)
            {
                this.computed = null;
                this.progressBar.Value = 0;
                this.progressLabel.Text = string.Format(CultureInfo.CurrentCulture,
                    Resources.ResponseMatrixFailed, ex.Message);
            }
            finally
            {
                this.SetBusy(false);
                if (this.cancellation != null)
                {
                    this.cancellation.Dispose();
                    this.cancellation = null;
                }
            }
        }

        void ShowProgress(ResponseMatrixProgress p)
        {
            if (this.IsDisposed)
            {
                return;
            }

            this.progressBar.Value = Math.Min(this.progressBar.Maximum, p.Done);
            this.progressLabel.Text = string.Format(CultureInfo.CurrentCulture,
                Resources.ResponseMatrixProgress, p.Done, p.Total, p.LastEnergyKev,
                Duration(p.ElapsedSeconds), Duration(p.RemainingSeconds));
        }

        void CancelClick(object sender, EventArgs e)
        {
            if (this.cancellation != null)
            {
                this.cancellation.Cancel();
            }
        }

        void SaveClick(object sender, EventArgs e)
        {
            if (this.computed == null || this.config == null)
            {
                return;
            }

            try
            {
                ResponseMatrixStore.Save(this.config.Guid, this.computed);
                this.progressLabel.Text = string.Format(CultureInfo.CurrentCulture,
                    Resources.ResponseMatrixSaved, ResponseMatrixStore.PathOf(this.config.Guid));
                this.saveButton.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Resources.ResponseMatrixTitle,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void SetBusy(bool value)
        {
            this.busy = value;
            this.computeButton.Enabled = !value && this.config != null && this.config.HasGeometry;
            this.cancelButton.Enabled = value;
            this.saveButton.Enabled = !value && this.computed != null;
            this.closeButton.Enabled = !value;
            this.minEnergyBox.Enabled = !value;
            this.maxEnergyBox.Enabled = !value;
            this.nodesBox.Enabled = !value;
            this.binBox.Enabled = !value;
            this.historiesBox.Enabled = !value;
            this.threadsBox.Enabled = !value;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Пока идёт счёт, закрывать нельзя: фоновая задача пишет в поля
            // формы, и закрытие оставило бы её работать в никуда.
            if (this.busy)
            {
                e.Cancel = true;
                if (this.cancellation != null)
                {
                    this.cancellation.Cancel();
                }

                return;
            }

            base.OnFormClosing(e);
        }
    }
}
