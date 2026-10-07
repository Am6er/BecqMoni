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
        Panel detailsPanel;
        string detailsText = "";
        ProgressBar progressBar;
        Button computeButton, cancelButton, saveButton, closeButton;
        NumericUpDown minEnergyBox, maxEnergyBox, nodesBox, binBox, historiesBox, threadsBox;

        // (`AMBER219`) Галка «Use Nvidia GPU», её панель (носитель подсказки) и подсказка.
        Panel gpuPanel;
        GpuCheckBox gpuCheck;
        ToolTip gpuTip;

        /// <summary>Ответ <see cref="RmGpu.Probe"/> при открытии окна; null — не спрашивали.</summary>
        GpuProbe gpuProbe;

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
        /// Матрица записана в склад (`AMBER48`). Вкладка Efficiency читает
        /// поколение матрицы из ЗАГОЛОВКА ФАЙЛА склада, а не из конфигурации, —
        /// после записи файл сменил поколение, и подпись «two generations side
        /// by side» обязана уйти тут же: вкладка видна за этим модальным окном
        /// (снимок Amber 18.09.2026, «Нажал сохранить… Предупреждение не
        /// исчезло»).
        /// </summary>
        public event EventHandler MatrixSaved;

        void OnMatrixSaved()
        {
            EventHandler handler = this.MatrixSaved;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        public ResponseMatrixForm(EfficiencyConfigData config)
        {
            this.config = config;
            this.BuildLayout();
            this.LoadExisting();
            this.ProbeGpu();
        }

        ResponseMatrixOptions CurrentOptions()
        {
            var options = new ResponseMatrixOptions
            {
                MinEnergyKev = (double)this.minEnergyBox.Value,
                MaxEnergyKev = (double)this.maxEnergyBox.Value,
                NodeCount = (int)this.nodesBox.Value,
                BinKev = (double)this.binBox.Value,
                Histories = (int)this.historiesBox.Value,
                Threads = (int)this.threadsBox.Value
            };

            // (`AMBER219`) GPU считает только плоско — решение Amber 07.10.2026,
            // дословно: «Плоско, как склад». Останов по шуму 3 % в клеймо не
            // входит (`ComputeStamp`), так что матрица с него и матрица CPU с
            // остановом — одного клейма, различаются лишь набранной статистикой.
            if (this.UseGpu)
            {
                options.ContinuumErrorTarget = 0.0;
            }

            return options;
        }

        /// <summary>Галка GPU стоит и годна — счёт идёт на устройстве.</summary>
        bool UseGpu
        {
            get
            {
                return this.gpuCheck != null && this.gpuCheck.Checked
                       && this.gpuProbe != null && this.gpuProbe.Available;
            }
        }

        /// <summary>
        /// (`AMBER219`) Годность GPU при открытии окна: негодная галка снята и
        /// выключена, причина — подсказкой на её панели; годная получает значение
        /// из общих настроек (`GlobalConfigInfo.UseGpuForResponseMatrix`, умолчание
        /// ВЫКЛ) и подсказку с именем карты. Сохранённое «вкл» на машине без GPU
        /// не трогается: это настройка приложения, а не этой машины.
        ///
        /// Подсказка собирается из ключей ресурсов по коду причины
        /// (<see cref="GpuProbeReason"/>), а не из английского текста пробы:
        /// текст пробы идёт в журнал, подпись окна обязана быть на языке окна.
        /// </summary>
        void ProbeGpu()
        {
            this.gpuProbe = RmGpu.Probe(RmGpu.DefaultPath);
            string tip;
            if (this.gpuProbe.Available)
            {
                tip = string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixGpuReady,
                                    this.gpuProbe.DeviceName,
                                    ComputeCapability(this.gpuProbe.ComputeCapability),
                                    CudaVersion(this.gpuProbe.DriverVersion));
            }
            else
            {
                switch (this.gpuProbe.ReasonCode)
                {
                    case GpuProbeReason.NoLibrary:
                        tip = Resources.ResponseMatrixGpuNoLibrary;
                        break;
                    case GpuProbeReason.LoadFailed:
                        tip = string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixGpuLoadFailed, this.gpuProbe.Reason);
                        break;
                    case GpuProbeReason.OldLibrary:
                        tip = Resources.ResponseMatrixGpuOldLibrary;
                        break;
                    case GpuProbeReason.PhysicsMismatch:
                        tip = string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixGpuPhysicsMismatch,
                                            this.gpuProbe.LibraryPhysics, ResponseMatrix.PhysicsVersion);
                        break;
                    case GpuProbeReason.NoDevice:
                        tip = Resources.ResponseMatrixGpuNoDevice;
                        break;
                    case GpuProbeReason.DriverTooOld:
                        tip = string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixGpuDriverTooOld,
                                            CudaVersion(this.gpuProbe.DriverVersion), CudaVersion(this.gpuProbe.RuntimeVersion));
                        break;
                    case GpuProbeReason.DeviceTooOld:
                        tip = string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixGpuDeviceTooOld,
                                            this.gpuProbe.DeviceName, ComputeCapability(this.gpuProbe.ComputeCapability),
                                            ComputeCapability(RmGpu.MinComputeCapability));
                        break;
                    default:
                        tip = string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixGpuRuntimeError, this.gpuProbe.Reason);
                        break;
                }
            }

            this.gpuTip.SetToolTip(this.gpuPanel, tip);
            this.gpuTip.SetToolTip(this.gpuCheck, tip);
            // Негодная галка остаётся живой для мыши (ради подсказки), но
            // рисуется выключенной и не переключается — `GpuCheckBox`.
            this.gpuCheck.Unavailable = !this.gpuProbe.Available;
            if (this.gpuProbe.Available)
            {
                GlobalConfigManager manager = GlobalConfigManager.GetInstance();
                this.gpuCheck.Checked = manager != null && manager.GlobalConfig != null
                                        && manager.GlobalConfig.UseGpuForResponseMatrix;
            }
        }

        /// <summary>«8.6» из 86 — как печатает `nvidia-smi`.</summary>
        static string ComputeCapability(int cc)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}", cc / 10, cc % 10);
        }

        /// <summary>«13.0» из 13000 — версия драйвера/рантайма CUDA числом рантайма.</summary>
        static string CudaVersion(int v)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}", v / 1000, (v % 1000) / 10);
        }

        /// <summary>
        /// Переключение галки — сразу в общие настройки и на диск (постановка
        /// Amber: «запоминается глобально в приложении»). Пока окно только
        /// раскладывается, галка выключена и сюда не попадает; выключенная
        /// (негодная) галка значение не пишет.
        /// </summary>
        void GpuCheckChanged(object sender, EventArgs e)
        {
            if (this.gpuProbe == null || !this.gpuProbe.Available)
            {
                return;
            }

            GlobalConfigManager manager = GlobalConfigManager.GetInstance();
            if (manager == null || manager.GlobalConfig == null)
            {
                return;
            }

            if (manager.GlobalConfig.UseGpuForResponseMatrix != this.gpuCheck.Checked)
            {
                manager.GlobalConfig.UseGpuForResponseMatrix = this.gpuCheck.Checked;
                manager.SaveConfigFile();
            }
        }

        // ------------------------------------------------------------------
        // Состояние
        // ------------------------------------------------------------------

        void LoadExisting()
        {
            // ⛔ ШТАТНОЕ ЧИСЛО ИСТОРИЙ СНИМАЕТСЯ С ПОЛЯ ДО ТОГО, КАК ЕГО ТРОНУТ.
            // На входе сюда поле стоит на умолчании разметки (3 млн, `A39`), и
            // это единственное место, где умолчание ещё известно: ниже поле
            // перепишется тем, чем посчитана прежняя матрица.
            decimal nominalHistories = this.historiesBox.Value;

            if (this.config == null || !this.config.HasGeometry)
            {
                this.stateLabel.Text = Resources.ResponseMatrixNoGeometry;
                this.ShowVersions(0, 0, false);
                this.SetDetails("");
                this.computeButton.Enabled = false;
                return;
            }

            // ⛔ (`A50`) ОТКАЗ ЧИТАТЬ МАТРИЦУ СПРАШИВАЕТСЯ У САМОГО ЧТЕНИЯ, а не
            // угадывается по заголовку. Причину знает `Load`, и она называет
            // себя (`MatrixRefusal`); форма обязана сказать её словами.
            MatrixRefusal refusal;
            int fileFormat;
            // (`AMBER186`) Матрица, записанная этим окном и ждущая сохранения
            // конфигурации прибора, новее склада — показывается она.
            this.shownSource = ResponseMatrixStore.EditingSource(this.config.Guid);
            ResponseMatrix existing = ResponseMatrixStore.Load(this.config.Guid, this.shownSource,
                                                              out refusal, out fileFormat);
            if (existing == null)
            {
                this.SayRefusal(refusal, fileFormat);

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

                // ⛔ ИСТОРИЙ — НЕ НИЖЕ ШТАТНОГО. Число историй — УСИЛИЕ, а не
                // содержание (так и сказано в `ComputeStamp` про цели останова),
                // и наследовать его от УСТАРЕВШЕЙ матрицы значит повторять её
                // шум. Найдено 05.09.2026 разбором `A122`: подъём умолчания до
                // 3 млн (`A39`) не доехал до кнопки «Пересчитать» — форма
                // подставляла 300 000 из прежнего файла, и `a6ac85bb` 04.09
                // получила вдесятеро меньше трёх соседей, поднятых рукой.
                // Границы, узлы и бин выше наследуются по-прежнему: они —
                // содержание, другая сетка даёт другую матрицу и другое клеймо.
                // Матрица, посчитанная ГУЩЕ штатного, тоже наследуется —
                // правило `T36` «посчитана гуще штатной — не понижать».
                SetClamped(this.historiesBox,
                           Math.Max(existing.Options.Histories, nominalHistories));
            }

            // E18 (б). Матрица есть — она и выигрывает: поля обязаны повторять
            // ТО, ЧЕМ ОНА ПОСЧИТАНА, иначе «Пересчитать» даст другую матрицу.
            // Но кривую с тех пор могли пересчитать в другом диапазоне, и это
            // расхождение надо НАЗЫВАТЬ, а не прятать: молчащее несогласие
            // ровно того сорта, из-за которого и заведена эта правка.
            this.SetDetails(this.Describe(existing) + this.DescribeRangeMismatch(existing)
                            + DescribeInheritedHistories(existing, nominalHistories)
                            + this.DescribePending());
        }

        /// <summary>
        /// (`AMBER186`) Откуда показана матрица: со склада или из временного
        /// файла, ждущего сохранения конфигурации прибора.
        /// </summary>
        ResponseMatrixSource shownSource = ResponseMatrixSource.Store;

        /// <summary>
        /// Матрицу записали во временный файл (`AMBER186`): вкладке прибора —
        /// пометить конфигурацию изменённой, иначе вопрос «сохранить?» не
        /// прозвучит, и временный снимется при закрытии формы молча.
        /// </summary>
        public bool MatrixSavedPending { get; private set; }

        /// <summary>Строка «ждёт сохранения конфигурации» — только у временной.</summary>
        string DescribePending()
        {
            return this.shownSource == ResponseMatrixSource.Pending
                ? Environment.NewLine + Resources.ResponseMatrixPendingNote
                : "";
        }

        /// <summary>
        /// Назвать СЛОВАМИ, почему матрица не прочиталась (`A50`, решение Amber
        /// 05.09.2026: «матрица формата 6, нужен 7 — пересчитайте»).
        ///
        /// Прежде форма причину не спрашивала, а ВЫВОДИЛА: заглядывала в
        /// заголовок сама (<see cref="ResponseMatrix.PeekVersions"/>) и на любой
        /// прочитанный заголовок говорила «устарела: посчитана другим
        /// поколением ПЕРЕНОСА». Это неправда дважды. У файла прежнего ФОРМАТА
        /// физика могла совпадать с нынешней — названа была не та причина, и
        /// номеров формата в предложении не стояло вовсе (они уходили в серую
        /// строку версий). У ОБРУБКА нынешнего формата совпадают и физика, и
        /// формат, а форма всё равно говорила «устарела» и красила строку
        /// версий красным — то есть указывала на согласные числа как на
        /// причину.
        ///
        /// Теперь причину называет само чтение, а заголовок читается только
        /// ради номера ФИЗИКИ для строки версий: в отказе его нет, а показать
        /// поколение матрицы человеку всё равно надо.
        /// </summary>
        void SayRefusal(MatrixRefusal refusal, int fileFormat)
        {
            int headerFormat, headerPhysics;
            bool header = ResponseMatrix.PeekVersions(ResponseMatrixStore.PathOf(this.config.Guid, this.shownSource),
                                                     out headerFormat, out headerPhysics);
            switch (refusal)
            {
                case MatrixRefusal.OldFormat:
                    this.stateLabel.Text = string.Format(CultureInfo.InvariantCulture,
                                                         Resources.ResponseMatrixStateOldFormat,
                                                         fileFormat, ResponseMatrix.FormatVersion);
                    this.ShowVersions(headerPhysics, fileFormat, true);
                    this.computeButton.Text = Resources.ResponseMatrixRecompute;
                    break;

                case MatrixRefusal.Unreadable:
                    // Файл наш и формат нынешний — чтение оборвалось на теле.
                    // Красным строку версий красить не за что: подсветка
                    // означает «поколения разошлись», а здесь они сошлись.
                    this.stateLabel.Text = Resources.ResponseMatrixStateUnreadable;
                    this.ShowVersions(headerPhysics, header ? headerFormat : 0,
                                      header && (headerPhysics != ResponseMatrix.PhysicsVersion
                                                 || headerFormat != ResponseMatrix.FormatVersion));
                    this.computeButton.Text = Resources.ResponseMatrixRecompute;
                    break;

                case MatrixRefusal.NotOurs:
                    // Метки `BQRM` нет — поколений у такого файла не бывает, и
                    // печатать нечего, кроме нынешних.
                    this.stateLabel.Text = Resources.ResponseMatrixStateNotOurs;
                    this.ShowVersions(0, 0, false);
                    this.computeButton.Text = Resources.ResponseMatrixCompute;
                    break;

                default:
                    // `NoFile` — матрицу этой геометрии не считали вовсе.
                    this.stateLabel.Text = Resources.ResponseMatrixStateMissing;
                    this.ShowVersions(0, 0, false);
                    this.computeButton.Text = Resources.ResponseMatrixCompute;
                    break;
            }
        }

        /// <summary>
        /// Строка о том, что прежняя матрица посчитана РЕЖЕ штатного числа
        /// историй и поле поднято до штатного (`A122`). Пусто, когда матрица не
        /// беднее штатной: «поднято до 3 000 000» у матрицы на 3 000 000
        /// объясняло бы то, чего не происходило.
        ///
        /// ✅ Текст — В РЕСУРСАХ (`A187`, 06.09.2026). До этого дня он выбирался
        /// здесь по <c>CurrentUICulture</c> своей парой литералов: 05.09.2026
        /// общий `Resources.resx` правила другая полоса, и трогать его было
        /// нельзя. Ключ — `ResponseMatrixInheritedHistories`, обе культуры.
        ///
        /// ⛔ `F0`, а не `N0` (`A244`, решение Amber 05.09.2026): группировки
        /// разрядов в приложении нет вовсе — `N0` на инварианте дал бы
        /// «3,000,000», запятую в группах. Формат живёт В ЗНАЧЕНИИ ресурса, и
        /// перевод обязан его сохранить.
        /// </summary>
        static string DescribeInheritedHistories(ResponseMatrix matrix, decimal nominalHistories)
        {
            if (matrix == null || matrix.Options == null
                || matrix.Options.Histories >= nominalHistories)
            {
                return "";
            }

            return Environment.NewLine
                   + string.Format(CultureInfo.InvariantCulture,
                                   Resources.ResponseMatrixInheritedHistories,
                                   matrix.Options.Histories, nominalHistories);
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
            return string.Format(CultureInfo.InvariantCulture,
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

            return Environment.NewLine + string.Format(CultureInfo.InvariantCulture,
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
                ? string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixVersionsBoth,
                                matrixPhysics, matrixFormat,
                                ResponseMatrix.PhysicsVersion, ResponseMatrix.FormatVersion)
                : string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixVersionsCurrent,
                                ResponseMatrix.PhysicsVersion, ResponseMatrix.FormatVersion);
            this.versionsLabel.ForeColor = mismatch ? Color.Firebrick : SystemColors.GrayText;
        }

        /// <summary>
        /// Число узлов в подробностях: сколько их СТАЛО и, если разошлось,
        /// сколько ЗАКАЗАНО в поле формы.
        ///
        /// Расходятся они законно и оба числа ВЕРНЫ: при <c>ResolveEdges</c>
        /// (умолчание) <c>BuildGrid(geometry)</c> добирает узлы вокруг K-краёв
        /// веществ пробы (`T42`), и сетка выходит длиннее заказанной — заказ 10
        /// даёт 12. На экране стояло одно число, и оно читалось как несогласие
        /// поля ввода с подробностями (`A93`).
        ///
        /// Вторая цифра печатается ТОЛЬКО при расхождении: «12 (заказано 12)»
        /// объясняло бы то, чего не происходило. У старой матрицы
        /// <c>Options</c> может не быть вовсе (по ней и поля формы не
        /// выставляются, см. <see cref="LoadExisting"/>) — тогда сравнивать не
        /// с чем, и печатается одно число.
        /// </summary>
        string DescribeNodes(ResponseMatrix matrix)
        {
            int made = matrix.NodeCount;
            if (matrix.Options == null || matrix.Options.NodeCount == made)
            {
                return made.ToString(CultureInfo.InvariantCulture);
            }

            return string.Format(CultureInfo.InvariantCulture,
                                 Resources.ResponseMatrixNodesRequested,
                                 made, matrix.Options.NodeCount);
        }

        string Describe(ResponseMatrix matrix)
        {
            long fileBytes = ResponseMatrixStore.FileSize(this.config.Guid, this.shownSource);
            return string.Format(CultureInfo.InvariantCulture, Resources.ResponseMatrixDetails,
                                 this.DescribeNodes(matrix),
                                 matrix.Energies[0],
                                 matrix.Energies[matrix.NodeCount - 1],
                                 matrix.BinKev,
                                 matrix.Histories,
                                 matrix.DataBytes / 1024.0,
                                 fileBytes / 1024.0,
                                 // ⚠ ЕДИНСТВЕННОЕ место файла, оставленное на
                                 // культуре потока (`A244`): это ДАТА, а не
                                 // число. Дробной части у неё нет, правило
                                 // Amber про разделитель её не касается, а
                                 // инвариант дал бы русскому пользователю
                                 // американский порядок «09/05/2026». Обратной
                                 // стороны у неё тоже нет: в файл время уходит
                                 // тиками (`CreatedUtc.Ticks`), а не текстом,
                                 // так что «записали точкой — прочли запятой»
                                 // здесь невозможно.
                                 matrix.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                                 matrix.BuildSeconds)
                   + DescribeFingerprint(matrix);
        }

        /// <summary>
        /// Строка про ОТПЕЧАТОК ТЕЛА (`A121`, решение Amber 05.09.2026): первые
        /// 16 знаков SHA-256 строк матрицы — чтобы «те же числа» проверялось
        /// глазами так же однострочно, как «то же клеймо» — версиями. Три
        /// состояния: у файла до 05.09.2026 хвоста нет («не записан»); у
        /// свежего — сходится; у правленого тела — НЕ СХОДИТСЯ, и это надо
        /// видеть. У только что посчитанной и ещё не сохранённой матрицы
        /// отпечатка нет вовсе — он снимается с байтов файла.
        ///
        /// ✅ Текст — В РЕСУРСАХ (`A187`, 06.09.2026), четырьмя ключами
        /// `ResponseMatrixFingerprint*`: подпись строки и три её состояния.
        /// Раньше выбирался здесь по <c>CurrentUICulture</c> — по той же
        /// причине, что у <see cref="DescribeInheritedHistories"/>.
        ///
        /// ⚠ Три состояния — три ОТДЕЛЬНЫХ ключа, а не склейка подписи с
        /// хвостом: в другом языке хвост может стоять перед числом, и склейка
        /// связала бы переводчику руки.
        /// </summary>
        static string DescribeFingerprint(ResponseMatrix matrix)
        {
            string value;
            if (matrix == null || string.IsNullOrEmpty(matrix.BodyFingerprint))
            {
                value = Resources.ResponseMatrixFingerprintNone;
            }
            else
            {
                string head = matrix.BodyFingerprint.Substring(0, 16) + "…";
                value = matrix.StoredBodyFingerprint == null
                    ? string.Format(CultureInfo.InvariantCulture,
                                    Resources.ResponseMatrixFingerprintNotStored, head)
                    : matrix.BodyFingerprintMatches
                        ? head
                        : string.Format(CultureInfo.InvariantCulture,
                                        Resources.ResponseMatrixFingerprintMismatch, head);
            }

            return Environment.NewLine
                   + string.Format(CultureInfo.InvariantCulture,
                                   Resources.ResponseMatrixFingerprint, value);
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
                ? string.Format(CultureInfo.InvariantCulture, "{0:%h}:{0:mm}:{0:ss}", span)
                : string.Format(CultureInfo.InvariantCulture, "{0:%m}:{0:ss}", span);
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
            // ⛔ (`AMBER219`) Решение «GPU или CPU» снимается с галки ДО `SetBusy`:
            // занятая форма галку выключает, а `UseGpu` читает именно `Enabled`
            // — снятое после, оно всегда говорило бы «CPU». Оплачено 07.10.2026
            // на первой же проверке экраном: цель останова стояла нулём (её
            // `CurrentOptions` снял вовремя), а счёт пошёл на ЦП плоско, 3 млн
            // историй на узел, — ни строка хода, ни загрузка GPU этого не прятали.
            bool useGpu = this.UseGpu;
            this.computingOnGpu = useGpu;
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
                CancellationToken token = this.cancellation.Token;
                ResponseMatrix matrix = await Task.Run(
                    () =>
                    {
                        if (!useGpu)
                        {
                            return ResponseMatrixBuilder.Build(geometry, options, progress, token);
                        }

                        // (`AMBER219`) Устройство берётся на время счёта и отдаётся
                        // в `finally`: контекст CUDA между счётами держать незачем,
                        // а живым должен быть один `RmGpu` на процесс.
                        using (var gpu = new RmGpu(RmGpu.DefaultPath, RmGpu.DefaultStackBytes))
                        {
                            return GpuBuild.Build(gpu, geometry, options, progress, token, null);
                        }
                    },
                    token);

                this.computed = matrix;
                this.progressBar.Value = this.progressBar.Maximum;
                this.progressLabel.Text = string.Format(CultureInfo.InvariantCulture,
                    Resources.ResponseMatrixDone, Duration(matrix.BuildSeconds));

                // Континуум набирается аналоговой веткой полной сферой, и на
                // дальней геометрии до кристалла доходит доля телесного угла:
                // пик остаётся точным, а континуум может оказаться шумом. Без
                // этой строки различить нечем — оценка ошибки, что стоит выше,
                // описывает пик (F23).
                if (matrix.ContinuumWeightedError > ContinuumNoiseWarnPercent)
                {
                    this.progressLabel.Text += string.Format(CultureInfo.InvariantCulture,
                        Resources.ResponseMatrixContinuumNoise,
                        // `f1`, а не `n1` (`A244`): группировки разрядов нет.
                        matrix.ContinuumWeightedError.ToString("f1", CultureInfo.InvariantCulture));
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
                // (`AMBER201`, Р4, мелочь G.6) Причина — цепочкой вложенных, а
                // не `ex.Message`. Узлы считаются в `Parallel.ForEach`, и отказ
                // внутри узла приходит `AggregateException` со своим общим
                // «Произошла одна или несколько ошибок» — до человека доходила
                // только эта фраза, а настоящая причина (вещество без таблиц,
                // негодная сцена) терялась.
                this.progressLabel.Text = string.Format(CultureInfo.InvariantCulture,
                    Resources.ResponseMatrixFailed, AppUi.Reason(ex));
            }
            finally
            {
                this.computingOnGpu = false;
                this.SetBusy(false);
                if (this.cancellation != null)
                {
                    this.cancellation.Dispose();
                    this.cancellation = null;
                }
            }
        }

        /// <summary>(`AMBER219`) Идущий счёт — на GPU; снимается с галки до `SetBusy`, читает строка хода.</summary>
        bool computingOnGpu;

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
            // (`AMBER219`) На GPU строка хода называет устройство: человек должен
            // видеть, ЧЕМ считается, — у CPU и GPU одна полоса и одни поля.
            this.progressLabel.Text = string.Format(CultureInfo.InvariantCulture,
                this.computingOnGpu ? Resources.ResponseMatrixGpuProgress : Resources.ResponseMatrixProgress,
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
                // ⛔ (`AMBER186`, решение Amber 05.10.2026 «Временный файл +
                // перенос») НЕ НА СКЛАД. Геометрия, для которой матрица
                // посчитана, живёт в клоне конфигурации прибора; на склад
                // матрицу переносит сохранение конфигурации, а отказ от него
                // временный файл снимает. Прежде запись шла прямо на склад, и
                // ответ «Нет» оставлял прежнюю геометрию при затёртой матрице.
                ResponseMatrixStore.SavePending(this.config.Guid, this.computed);
                this.savedComputed = this.computed;
                this.shownSource = ResponseMatrixSource.Pending;
                this.MatrixSavedPending = true;
                this.progressLabel.Text = string.Format(CultureInfo.InvariantCulture,
                    Resources.ResponseMatrixSaved,
                    ResponseMatrixStore.PathOf(this.config.Guid, ResponseMatrixSource.Pending));
                this.saveButton.Enabled = false;
                // Отпечаток тела появляется при ЗАПИСИ (`A121`) — подробности
                // после неё обязаны его показать, а не «нет».
                this.SetDetails(this.Describe(this.computed) + this.DescribePending());
                this.OnMatrixSaved();
            }
            catch (Exception ex)
            {
                // `AppUi.Report`, не `MessageBox.Show`: обработчик назван в
                // пробе `CurveGenerationProbe` (`AMBER48`) и стоит на
                // безоконном пути (`S100`) — модальное окно там повесило бы
                // пробу; в приложении `Report` показывает то же окно.
                AppUi.Report(ex.Message, Resources.ResponseMatrixTitle, MessageBoxIcon.Warning);
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
            // (`AMBER219`) Галка GPU на время счёта тоже замирает (настоящим
            // `Enabled`, подсказка на время счёта не нужна); негодная после
            // счёта снова живая для мыши, но нарисована выключенной.
            this.gpuCheck.Enabled = !value;
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

            // (`AMBER201`, Р4, подозрение полосы 9) Посчитанная и НЕ сохранённая
            // матрица — вопрос перед закрытием. Прежде окно закрывалось молча, и
            // часы счёта пропадали одним нажатием «Закрыть» или крестика.
            // Окно модальное: закрытие кнопкой приходит с `CloseReason.None`.
            if (this.computed != null && !object.ReferenceEquals(this.computed, this.savedComputed)
                && (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.None)
                && AppUi.HasWindows)
            {
                string text = Resources.ResourceManager.GetString("ResponseMatrixCloseUnsaved", Resources.Culture);
                if (string.IsNullOrEmpty(text))
                {
                    text = "The computed response matrix is not saved." + Environment.NewLine
                           + Environment.NewLine + "Close the window and discard it?";
                }

                if (MessageBox.Show(this, text, this.Text, MessageBoxButtons.YesNo,
                                    MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnFormClosing(e);
        }

        /// <summary>(`AMBER201`) Посчитанная матрица, уже записанная «Сохранить».</summary>
        ResponseMatrix savedComputed;
    }
}
