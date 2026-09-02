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

        Label stateLabel, versionsLabel, progressLabel;
        CheckBox useMatrixCheck;
        Panel detailsPanel;
        string detailsText = "";
        ProgressBar progressBar;
        Button computeButton, cancelButton, saveButton, closeButton;
        NumericUpDown minEnergyBox, maxEnergyBox, nodesBox, binBox, historiesBox, threadsBox;

        /// <summary>
        /// С какой ошибки интеграла континуума строки предупреждать, %.
        ///
        /// Считается по ВЗВЕШЕННОЙ величине
        /// (<see cref="EfficiencyMaker.ResponseMatrix.ContinuumWeightedError"/>), а не
        /// по худшему узлу: довод «на контактной геометрии узел набирает доли
        /// процента» измерением не подтвердился — верх шкалы голодает и на
        /// контакте (11.25 % против 3.6 % на 662 той же геометрии), и порог 2 %
        /// по худшему узлу горел ВСЕГДА. Предупреждение, которое горит всегда,
        /// никто не читает (T15).
        ///
        /// Величина порога взята по замеру: цилиндр на 50 мм, умолчания (100
        /// узлов, 300 тыс. историй) дают 4.57 % взвешенной при 20.00 % худшей.
        /// То есть при умолчаниях на обычной геометрии порог молчит, а вчетверо
        /// большее число историй уводит величину к 2.3 % — предупреждение
        /// гаснет ровно от того действия, которое само же и советует.
        /// </summary>
        const double ContinuumNoiseWarnPercent = 5.0;

        CancellationTokenSource cancellation;
        ResponseMatrix computed;
        bool busy;

        // Номер ПОСЛЕДНЕГО запроса оценки времени. Загрузка формы дёргает
        // ValueChanged у пяти полей подряд, и без номера ярлык доставался
        // последней ФИНИШИРОВАВШЕЙ задаче, а не последней запрошенной.


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

                // E18. Матрицы ещё нет — а именно за первым расчётом форму и
                // открывают. Раньше поля стояли на умолчаниях разметки, и
                // человек, выставивший кривой нижнюю границу 20 кэВ, молча
                // получал матрицу с 30: кривая и матрица описывают ОДИН прибор
                // в ОДНОЙ геометрии, и разъехавшийся диапазон — не выбор, а
                // недосмотр. Границы берём у кривой.
                this.SetDetails(this.ApplyCurveRange());
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

            // E18 (б). Матрица есть — она и выигрывает: поля обязаны повторять
            // ТО, ЧЕМ ОНА ПОСЧИТАНА, иначе «Пересчитать» даст другую матрицу.
            // Но кривую с тех пор могли пересчитать в другом диапазоне, и это
            // расхождение надо НАЗЫВАТЬ, а не прятать: молчащее несогласие
            // ровно того сорта, из-за которого и заведена эта правка.
            this.SetDetails(this.Describe(existing) + this.DescribeRangeMismatch(existing));
        }

        static void SetClamped(NumericUpDown box, decimal value)
        {
            box.Value = Math.Min(box.Maximum, Math.Max(box.Minimum, value));
        }

        /// <summary>
        /// Края кривой эффективности этой конфигурации, кэВ. Ложь — кривой нет
        /// (<see cref="EfficiencyConfigData.HasCurve"/>) или все точки в одной
        /// энергии, то есть диапазона из неё не выходит.
        ///
        /// Берётся МИНИМУМ и МАКСИМУМ, а не первая и последняя точка: порядок
        /// списка — соглашение, а не проверяемое свойство, и кривая, введённая
        /// руками или собранная из нескольких источников, может прийти
        /// неотсортированной. Цена проверки — один проход по десяткам точек.
        /// </summary>
        bool CurveRange(out double lo, out double hi)
        {
            lo = 0.0;
            hi = 0.0;
            if (this.config == null || !this.config.HasCurve)
            {
                return false;
            }

            bool any = false;
            foreach (ROIEfficiencyData point in this.config.Curve)
            {
                if (point == null || !(point.Energy > 0.0))
                {
                    continue;
                }

                if (!any || point.Energy < lo) lo = point.Energy;
                if (!any || point.Energy > hi) hi = point.Energy;
                any = true;
            }

            return any && hi > lo;
        }

        /// <summary>
        /// E18 (а): подставить в поля диапазон кривой, когда матрицы ещё нет.
        /// Возвращает строку для подробностей — пусто, если подставлять нечего.
        ///
        /// Берутся ТОЛЬКО границы. Число историй у кривой в клейме есть
        /// (`hist=` в <see cref="EfficiencyConfigData.ComputeStamp"/>), а узлы и
        /// бин выводятся из её сетки, но переносить их нельзя: кривая — один
        /// вектор, матрица — квадрат, и та же статистика на узел стоит здесь на
        /// порядки дороже. Умолчания формы для них подобраны замером (см.
        /// <see cref="ContinuumNoiseWarnPercent"/>), а границы — это не цена
        /// счёта, а постановка задачи: диапазон, в котором прибор описан.
        /// </summary>
        string ApplyCurveRange()
        {
            double lo, hi;
            if (!this.CurveRange(out lo, out hi))
            {
                // (в) кривой нет вовсе — остаются прежние умолчания разметки.
                return "";
            }

            SetClamped(this.minEnergyBox, (decimal)lo);
            SetClamped(this.maxEnergyBox, (decimal)hi);
            return string.Format(CultureInfo.CurrentCulture,
                                 Resources.ResponseMatrixRangeFromCurve, lo, hi);
        }

        /// <summary>
        /// E18 (б): строка о расхождении диапазонов кривой и готовой матрицы.
        /// Пусто, когда кривой нет или края сходятся. Порог — полкэВ: узлы
        /// матрицы кладутся по логарифмической сетке, и точное равенство краёв
        /// не гарантировано даже при одинаковой постановке.
        /// </summary>
        string DescribeRangeMismatch(ResponseMatrix matrix)
        {
            double lo, hi;
            if (matrix == null || matrix.NodeCount < 1 || !this.CurveRange(out lo, out hi))
            {
                return "";
            }

            double mlo = matrix.Energies[0];
            double mhi = matrix.Energies[matrix.NodeCount - 1];
            if (Math.Abs(mlo - lo) < 0.5 && Math.Abs(mhi - hi) < 0.5)
            {
                return "";
            }

            return Environment.NewLine + string.Format(CultureInfo.CurrentCulture,
                                                       Resources.ResponseMatrixRangeDiffers,
                                                       lo, hi, mlo, mhi);
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

        // ⛔ (`A46`) ПРЕДВАРИТЕЛЬНОЙ ОЦЕНКИ ВРЕМЕНИ БОЛЬШЕ НЕТ — решение Amber
        // 02.09.2026 «убирай ETA, оно всегда врёт». Здесь стоял
        // `UpdateEstimateAsync`, считавший её в фоне при каждой правке поля
        // (полторы-две секунды на каждую). Разбор, почему точной она стать не
        // могла, — в `A44`; код в коммите 818732b2.
        //
        // ⚠ `Duration` остался: время СЧИТАННОЙ матрицы («Done in 5:06», «took
        // 57 s» в её свойствах) — это факт о результате, а не прогноз хода.

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

            // Полоса — в ТЫСЯЧНЫХ ДОЛЯХ, а не в узлах (`W27`). Узлами её мерить
            // нельзя по двум причинам сразу: при останове по шуму узел
            // проходится до трёх раз, и полоса замирала полной с сотого прогона
            // из трёхсот; а сетка при `ResolveEdges` (умолчание) добирает узлы
            // на К-краях вещества, и `NodeCount` из поля формы меньше
            // фактической длины сетки даже при одном проходе.
            this.progressBar.Maximum = ProgressScale;

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
                if (matrix.ContinuumWeightedError > ContinuumNoiseWarnPercent)
                {
                    this.progressLabel.Text += string.Format(CultureInfo.CurrentCulture,
                        Resources.ResponseMatrixContinuumNoise,
                        matrix.ContinuumWeightedError.ToString("n1", CultureInfo.CurrentCulture));
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

        /// <summary>
        /// Делений у полосы хода. Доля идёт по ДОСЧИТАННЫМ УЗЛАМ (`A46`):
        /// досчитанный узел досчитан навсегда, поэтому полоса движется только
        /// вперёд. Прежде она шла по цене узлов в потокосекундах и пятилась
        /// назад, когда узел просил второго прохода.
        /// </summary>
        const int ProgressScale = 1000;

        void ShowProgress(ResponseMatrixProgress p)
        {
            if (this.IsDisposed)
            {
                return;
            }

            int value = (int)Math.Round(p.Percent * (ProgressScale / 100.0));
            this.progressBar.Value = Math.Min(this.progressBar.Maximum, Math.Max(0, value));

            // ⛔ (`A46`) ВРЕМЕНИ В СТРОКЕ НЕТ — решение Amber 02.09.2026. Стоит
            // число узлов, ВЗЯТЫХ В РАБОТУ, из общего числа узлов сетки: оно
            // постоянно, в отличие от числа прогонов, которое росло по ходу
            // (140 → 155 → 156 → 157 на снимках одного расчёта).
            this.progressLabel.Text = string.Format(CultureInfo.CurrentCulture,
                Resources.ResponseMatrixProgress,
                p.StartedNodes, p.TotalNodes, p.LastEnergyKev);
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
