using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// (`A145`, этап 2) СЕАНС ПОЛНОСПЕКТРАЛЬНОГО РАЗБОРА — расчётная половина
    /// прежнего <c>FsaOverlay</c>, без единой обязанности UI.
    ///
    /// Принадлежит ДОКУМЕНТУ (<see cref="DocEnergySpectrum"/>), а не окну:
    /// график и будущее окно отчёта подписываются на один и тот же сеанс и
    /// строят представление из одного снимка результата
    /// (<see cref="FsaPresentationBuilder"/>). Второго запуска разбора и
    /// второго кэша быть не должно — это критерий 4 приёмки `A145`.
    ///
    /// Что здесь живёт: запуск в фоне, отпечаток входных данных
    /// (<see cref="BuildStamp(ResultData, bool, FsaCalculationOptions)"/>),
    /// кэш последнего результата, защита поколением, решение о матрице
    /// отклика и строка состояния. Чего здесь НЕТ и быть не должно: слоёв,
    /// цветов, строк таблицы, группировки родители/дочерние — всё это
    /// представление, и на отпечаток оно не влияет (критерий 7).
    ///
    /// Считать синхронно нельзя: полный проход по сетке дрейфа занимает
    /// десятые доли секунды, а перерисовка графика идёт по таймеру набора,
    /// и фит на UI-потоке подвесил бы окно на каждом обновлении. Поэтому
    /// результат кэшируется по отпечатку и пересчитывается только когда
    /// отпечаток сменился.
    ///
    /// ⛔ ПРАВИЛО ПОСЛЕДНЕГО СНИМКА (критерий 10). Если во время счёта входные
    /// данные сменились ещё раз (быстрая серия переключений), новый снимок
    /// становится в ОЧЕРЕДЬ ИЗ ОДНОГО МЕСТА (<see cref="pending"/>): каждый
    /// следующий вытесняет предыдущий, а по окончании текущего счёта очередь
    /// запускается сама. Результат счёта, который к моменту окончания уже
    /// вытеснен, НЕ ПУБЛИКУЕТСЯ — промежуточное значение никогда не остаётся
    /// «актуальным», и на серию из N переключений приходится ровно одна
    /// публикация, с отпечатком последнего снимка. Смена спектра
    /// (<see cref="Reset"/>) поднимает поколение, и вернувшийся счёт чужого
    /// поколения тоже молчит.
    /// </summary>
    public sealed class FsaAnalysisSession
    {
        readonly object sync = new object();

        FsaResult result;
        string stamp = "";
        bool running;
        string status;

        /// <summary>Снимок, ожидающий окончания текущего счёта (не более одного).</summary>
        Job pending;

        /// <summary>Отпечаток снимка, который считается сейчас.</summary>
        string activeStamp;

        // (`A50`) Матрица отклика ЕСТЬ, но прежнего формата. Держится отдельно
        // от результата: решение «с матрицей или без» принимается ДО фонового
        // счёта, а сказать о нём надо и тогда, когда счёт ничего не вернул.
        bool matrixOldFormat;

        // Заготовленное человеку сообщение и ключ уже сказанного. Ключ нужен,
        // потому что снимок берётся при каждом устаревании отпечатка — на
        // каждый тик набора, — а окно об одном и том же файле человек обязан
        // увидеть ОДИН раз.
        string matrixNotice;
        string matrixNoticeSaid;

        // Поколение результата. Сброс (смена активного спектра) его увеличивает,
        // и уже запущенный счёт, вернувшись, увидит чужой номер и промолчит:
        // иначе разложение прежнего спектра воскресало бы поверх нового уже
        // ПОСЛЕ сброса, и забыть его было бы нечем.
        int generation;

        /// <summary>
        /// Задвижка ПРОБ: пока она поднята и не сигналит, фоновый счёт стоит
        /// перед самым разбором. Без неё гонку «сменили спектр посреди счёта»
        /// не воспроизвести: разбор малого спектра занимает десятки
        /// миллисекунд, и проба всегда опаздывает. Ставится отражением
        /// (`FsaSessionProbe`); в приложении всегда null и не стоит ничего.
        /// </summary>
        static WaitHandle probeGate = null;

        /// <summary>Готовое разложение или null, пока его нет.</summary>
        public FsaResult Result
        {
            get
            {
                lock (this.sync)
                {
                    return this.result;
                }
            }
        }

        /// <summary>Отпечаток входных данных, которому отвечает <see cref="Result"/>; пусто — результата нет.</summary>
        public string Stamp
        {
            get
            {
                lock (this.sync)
                {
                    return this.stamp;
                }
            }
        }

        /// <summary>Идёт расчёт (в том числе стоящий в очереди следующий снимок).</summary>
        public bool IsRunning
        {
            get
            {
                lock (this.sync)
                {
                    return this.running;
                }
            }
        }

        /// <summary>Сообщение для полки графика: «считается», причина отказа.</summary>
        public string Status
        {
            get
            {
                lock (this.sync)
                {
                    return this.status;
                }
            }
        }

        /// <summary>
        /// (`A50`) У кривой ЛЕЖИТ матрица, но прежнего формата: разложение
        /// считается без неё. Отличается от «матрицы нет вовсе» тем, что
        /// лечится пересчётом, а не расчётом с нуля, — и строка качества
        /// обязана говорить об этом отдельной пометкой.
        /// </summary>
        public bool ResponseMatrixOldFormat
        {
            get
            {
                lock (this.sync)
                {
                    return this.matrixOldFormat;
                }
            }
        }

        /// <summary>
        /// Забрать заготовленное человеку сообщение об отвергнутой матрице —
        /// ОДИН раз на файл. Пусто, если говорить не о чем или уже сказано.
        ///
        /// ⛔ Почему сообщение забирают, а не показывают на месте: решение о
        /// матрице принимается при снимке, а тот зовётся из подготовки данных
        /// вида, то есть ИЗ ОТРИСОВКИ (<c>OnPaint</c> → <c>EnsureViewData</c> →
        /// <c>PrepareViewData</c>). Модальное окно там прокачивает очередь
        /// сообщений и входит в отрисовку повторно. Поэтому потребитель
        /// забирает строку в обработчике <see cref="Completed"/>, уже вне
        /// отрисовки, и показывает её сам.
        /// </summary>
        public string TakeResponseMatrixNotice()
        {
            lock (this.sync)
            {
                string notice = this.matrixNotice;
                this.matrixNotice = null;
                return notice;
            }
        }

        /// <summary>
        /// Счёт закончился и очередь пуста — потребителям пора перечитать
        /// <see cref="Result"/>/<see cref="Status"/>. Приходит из ФОНОВОГО
        /// потока; на серию переключений приходит один раз.
        /// </summary>
        public event EventHandler Completed;

        /// <summary>
        /// Забыть результат: он принадлежит прежнему спектру. Поднимает
        /// поколение — идущий счёт по возвращении промолчит.
        /// </summary>
        public void Reset()
        {
            lock (this.sync)
            {
                this.result = null;
                this.stamp = "";
                this.pending = null;
                this.status = null;
                this.generation++;
            }
        }

        /// <summary>
        /// Обесценить кэш, НЕ трогая результат: следующий
        /// <see cref="EnsureUpToDate(ResultData, bool)"/> посчитает заново.
        /// Для случая «параметры сменились, а потребителя нет»: тяжёлый счёт
        /// откладывается до его появления (`A145`, «Связь окна с главным
        /// интерфейсом»).
        /// </summary>
        public void Invalidate()
        {
            lock (this.sync)
            {
                this.stamp = "";
            }
        }

        /// <summary>
        /// Соответствует ли готовый результат этим входным данным. Считает
        /// отпечаток, поэтому звать с UI-потока.
        /// </summary>
        public bool IsUpToDate(ResultData resultData, bool subtractBackground)
        {
            if (resultData == null || resultData.EnergySpectrum == null || resultData.EnergySpectrum.Spectrum == null)
            {
                return false;
            }

            string currentStamp = BuildStamp(resultData, subtractBackground, FsaCalculationOptions.Of(resultData));
            lock (this.sync)
            {
                return !this.running && currentStamp == this.stamp;
            }
        }

        /// <summary>
        /// Убедиться, что разложение соответствует текущему спектру, и запустить
        /// расчёт, если нет. Настройки расчёта снимаются с активной копии
        /// конфигурации спектра (<see cref="FsaCalculationOptions.Of"/>).
        /// Вызывать с UI-потока: снимок списка нуклидов и конфигураций
        /// снимается здесь, в фон уходят уже копии.
        /// </summary>
        public void EnsureUpToDate(ResultData resultData, bool subtractBackground)
        {
            this.EnsureUpToDate(resultData, subtractBackground, FsaCalculationOptions.Of(resultData));
        }

        /// <summary>
        /// То же — с явным снимком настроек расчёта. Пять флажков `*ForFsa`,
        /// источник состава и равновесие доезжают до анализатора ТОЛЬКО этой
        /// дорогой (<see cref="FsaCalculationOptions.ApplyTo(FsaAnalyzer)"/>,
        /// <see cref="FsaCalculationOptions.ApplyTo(FsaSampleSpec)"/>), и их
        /// отпечаток (<see cref="FsaCalculationOptions.Stamp"/>) — часть общего.
        /// </summary>
        public void EnsureUpToDate(ResultData resultData, bool subtractBackground, FsaCalculationOptions options)
        {
            if (resultData == null || resultData.EnergySpectrum == null || resultData.EnergySpectrum.Spectrum == null)
            {
                return;
            }

            if (options == null)
            {
                options = FsaCalculationOptions.Of(resultData);
            }

            string currentStamp = BuildStamp(resultData, subtractBackground, options);
            int myGeneration;
            lock (this.sync)
            {
                if (this.running)
                {
                    // Считается ровно это — очередь не нужна (и если там
                    // лежало что-то другое, оно больше не нужно тоже).
                    if (currentStamp == this.activeStamp)
                    {
                        this.pending = null;
                        return;
                    }

                    // Это уже стоит в очереди.
                    if (this.pending != null && this.pending.Stamp == currentStamp)
                    {
                        return;
                    }
                }
                else if (currentStamp == this.stamp)
                {
                    return;
                }

                myGeneration = this.generation;
            }

            // Со снятого флага и до Task.Run всё идёт под try: снимок трогает
            // менеджеры и списки, и брошенное здесь исключение ушло бы в
            // отрисовку, у которой обработчика нет.
            Job job;
            try
            {
                job = this.Capture(resultData, subtractBackground, options, currentStamp, myGeneration);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("FSA start failed: " + ex);
                string said = FailureText(ex);
                lock (this.sync)
                {
                    this.status = said;
                }

                return;
            }

            lock (this.sync)
            {
                if (this.running)
                {
                    // Очередь из одного места: последний снимок вытесняет
                    // предыдущий (критерий 10).
                    this.pending = job;
                    this.status = Properties.Resources.FSACalculating;
                    return;
                }

                this.Start(job);
            }
        }

        /// <summary>
        /// Сколько раз сеанс ЗАПУСКАЛ фоновый счёт за всё время жизни.
        /// Читатель — приёмка `A145` (критерии 4 и 7): «переключение
        /// родители/дочерние не запускает FSA» и «расчётный флаг даёт ровно
        /// один пересчёт» доказываются этим числом, а не отсутствием
        /// событий — событие приходит один раз на серию, и пересчёт, съеденный
        /// очередью, через него не виден.
        /// </summary>
        public int RunCount
        {
            get
            {
                lock (this.sync)
                {
                    return this.runCount;
                }
            }
        }

        int runCount;

        /// <summary>Запуск под <see cref="sync"/>.</summary>
        void Start(Job job)
        {
            this.running = true;
            this.runCount++;
            this.activeStamp = job.Stamp;
            this.status = Properties.Resources.FSACalculating;
            Task.Run(() => this.Compute(job));
        }

        /// <summary>
        /// ⛔ ОТКАЗ РАЗЛОЖЕНИЯ НАЗЫВАЕТ ПРИЧИНУ (`A95`).
        ///
        /// Оба перехвата — подготовки (<see cref="EnsureUpToDate(ResultData, bool, FsaCalculationOptions)"/>)
        /// и самого счёта — писали в строку состояния ОДИН И ТОТ ЖЕ текст
        /// «Полноспектральное разложение не удалось, подробности в журнале», а
        /// причина уходила только в <see cref="Trace"/>, которого при обычном
        /// запуске никто не читает. Отсюда разряд дефекта, стоивший заходов:
        /// снимок спектра без энергетической калибровки падал пустым NRE, и
        /// обе половины опыта молчали одинаково — положительный контроль
        /// проходил ВПУСТУЮ.
        ///
        /// Слова собираются той же дверью, что у отвергнутой матрицы (`A50`) и
        /// у менеджеров-одиночек (`A22`, `A25`): <see cref="AppUi.Reason"/> —
        /// единственное соглашение о том, как называется причина, и оно
        /// обязательно с ВЛОЖЕННЫМ исключением. Второго заводить нельзя.
        ///
        /// Читателей у признака два, и оба уже есть: без окон — поток ошибок
        /// (<see cref="AppUi.Note"/>), его видят пробы и корпусные прогоны; с
        /// окнами — сама строка состояния, которую вид печатает под графиком с
        /// переносом по ширине. Модальным окном здесь сказать нельзя:
        /// <c>EnsureUpToDate</c> зовётся из подготовки данных вида, то есть
        /// изнутри отрисовки (см. <see cref="NoteOldMatrixFormat"/>).
        ///
        /// Подпись причины (<c>ERRFailureReason</c>) переведена в обе культуры;
        /// сам текст исключения приходит от платформы и переводу не подлежит.
        /// </summary>
        static string FailureText(Exception ex)
        {
            string text = Properties.Resources.FSAFailed + " "
                          + string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                          Properties.Resources.ERRFailureReason,
                                          AppUi.Reason(ex));
            AppUi.Note(text);
            return text;
        }

        /// <summary>
        /// Всё, что фоновому счёту нужно от UI-потока, — одним снимком.
        /// Снимается целиком ДО <c>Task.Run</c>: во время набора UI
        /// перезаписывает <c>Spectrum[]</c>, правит списки нуклидов и
        /// конфигурации, и фит, читающий живые объекты, собрал бы левую
        /// половину модели по старому спектру, правую — по новому.
        /// </summary>
        sealed class Job
        {
            public string Stamp;
            public int Generation;
            public EnergySpectrum Spectrum;
            public EnergySpectrum Background;
            public FwhmCalibration FwhmCalibration;
            public FsaEfficiency Efficiency;
            public ResultData CompositionInput;
            public List<NuclideDefinition> Definitions;
            public List<Peak> Peaks;
            public FsaAnalyzer Analyzer;
            public FsaCalculationOptions Options;
            public Dictionary<int, double> CrystalFractions;
        }

        Job Capture(ResultData resultData, bool subtractBackground, FsaCalculationOptions options,
                    string currentStamp, int myGeneration)
        {
            Job job = new Job { Stamp = currentStamp, Generation = myGeneration, Options = options };

            // Снимок самого измерения обязателен: во время набора UI
            // перезаписывает Spectrum[], а FSA ниже читает его в Task.Run.
            // Калибровки входят в тот же снимок по той же причине.
            job.Spectrum = resultData.EnergySpectrum.Clone();
            job.Background = subtractBackground && resultData.BackgroundEnergySpectrum != null
                ? resultData.BackgroundEnergySpectrum.Clone()
                : null;
            job.FwhmCalibration = resultData.FwhmCalibration != null
                ? resultData.FwhmCalibration.Clone()
                : null;
            // Кривая эффективности: сначала СВОЯ кривая спектра — та, что
            // выбрана в панели измерения и лежит в его файле. Кривая переехала
            // из набора зон в конфигурацию прибора, и разложение обязано брать
            // её оттуда же, откуда её берёт активность: две разные кривые в
            // одном спектре — два разных ответа на один вопрос.
            EfficiencyConfigData efficiencyConfig = resultData.Efficiency != null
                ? resultData.Efficiency.Copy()
                : null;
            job.Efficiency = FsaEfficiency.FromConfig(efficiencyConfig);
            job.CompositionInput = CompositionInput(resultData.PeakDetectionMethodConfig,
                                                    efficiencyConfig, job.Spectrum, job.FwhmCalibration);

            // Снимок списков: их правит UI-поток (конструктор сетов, NucBase),
            // а перечисление живого списка в фоне ловит «Collection was modified».
            NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
            job.Definitions = new List<NuclideDefinition>(nuclideManager.NuclideDefinitions);
            job.Peaks = resultData.DetectedPeaks != null
                ? new List<Peak>(resultData.DetectedPeaks)
                : new List<Peak>();

            FsaAnalyzer analyzer = new FsaAnalyzer();

            // (`A170`) Пользовательские настройки — во внутренние ключи ОДНИМ
            // фасадом; `BackscatterWithMatrix` при этом опускается, `EscapeGate`
            // не трогается.
            options.ApplyTo(analyzer);

            // Матрица отклика берётся у ТОЙ ЖЕ кривой, что и эффективность, и
            // только если её отпечаток сходится с нынешней геометрией. Не
            // сошёлся — работаем без неё, старым путём: посчитать спектр по
            // матрице чужой геометрии хуже, чем не посчитать вовсе.
            // UseResponseMatrix — выключатель пользователя (W11, галка в форме
            // «Матрица отклика»): выключено — считаем без матрицы, файл даже
            // не читаем.
            bool oldFormat = false;
            if (efficiencyConfig != null && efficiencyConfig.HasGeometry
                && efficiencyConfig.UseResponseMatrix)
            {
                // ⛔ (`A50`) ОТКАЗ ЧИТАТЬ МАТРИЦУ ОБЯЗАН НАЗЫВАТЬ СЕБЯ. Прежде
                // `Load` возвращал `null` молча — и «файла нет» было
                // неотличимо от «файл есть, но посчитан прежним форматом»: в
                // легенде обоим доставалась одна пометка «· без матрицы».
                // Лечится это по-разному (посчитать против пересчитать), и
                // сказать человеку, что именно с ним случилось, было нечем.
                EfficiencyMaker.MatrixRefusal refusal;
                int fileFormat;
                EfficiencyMaker.ResponseMatrix matrix =
                    EfficiencyMaker.ResponseMatrixStore.Load(efficiencyConfig.Guid,
                                                             out refusal, out fileFormat);
                if (refusal == EfficiencyMaker.MatrixRefusal.OldFormat)
                {
                    oldFormat = true;
                    this.NoteOldMatrixFormat(efficiencyConfig, fileFormat);
                }

                if (matrix != null && matrix.IsValidFor(efficiencyConfig.Geometry))
                {
                    analyzer.ResponseMatrix = matrix;

                    // Вещество кристалла идёт вместе с матрицей и только с ней:
                    // им каскадное суммирование ставит сумм-пики по сумме СВЕТА
                    // (S20), а без матрицы суммирования нет вовсе.
                    analyzer.ScintillatorMaterial =
                        EfficiencyMaker.EfficiencySimulator.ScintillatorNameOf(
                            efficiencyConfig.Geometry);
                }
            }

            // Решение о матрице — сразу, как и прежде: о нём спрашивают и до
            // того, как счёт вернулся (`ResponseMatrixFormProbe`).
            lock (this.sync)
            {
                this.matrixOldFormat = oldFormat;
            }

            analyzer.CoincidenceWindowSec = DeadTimeOf(resultData);

            if (resultData.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig peakConfig)
            {
                // Диапазон поиска пиков передаётся анализатору, но при
                // FitWholeSpectrum (умолчание) он им не пользуется — читает его
                // только запасной знаменатель при вырожденной калибровке.
                analyzer.MinEnergy = peakConfig.Min_Range;
                analyzer.MaxEnergy = peakConfig.Max_Range;
            }

            job.Analyzer = analyzer;

            // Вещество кристалла — СНИМКОМ и на UI-потоке, как всё прочее
            // (`S119`). Образам вылета от него нужна только доля рождения пар
            // (`S122`), а она считается по массовым долям элементов; плотность
            // в отношение не входит. Геометрии нет — снимка нет, и отбор
            // родителей остаётся прежним.
            if (efficiencyConfig != null && efficiencyConfig.HasGeometry
                && efficiencyConfig.Geometry.Crystal != null)
            {
                job.CrystalFractions = new Dictionary<int, double>(
                    efficiencyConfig.Geometry.Crystal.Fractions);
            }

            return job;
        }

        /// <summary>Фоновая половина: библиотека, разбор, публикация.</summary>
        void Compute(Job job)
        {
            FsaResult computed = null;
            string message = null;
            try
            {
                WaitHandle gate = probeGate;
                if (gate != null)
                {
                    gate.WaitOne();
                }

                // ⛔ Сборка библиотеки здесь ОДНА на обе ветки, и вторую
                // заводить нельзя. Развилка касается только того, ОТКУДА
                // берётся состав: подписи пиков как есть (прежний путь) или
                // цепочка родителя из баз (`S57`). Собирает образы в обоих
                // случаях `FsaSampleLibrary`/`FsaLibrary` — двух сборок с
                // разными правилами о линиях и рентгене в проекте быть не
                // должно.
                List<FsaComponent> library;
                if (job.Options.DbLookups)
                {
                    FsaCompositionInference.Report inferred;
                    FsaSampleSpec spec = FsaCompositionInference.Infer(job.Peaks, job.CompositionInput, out inferred);
                    job.Options.ApplyTo(spec);
                    Trace.WriteLine("FSA composition: " + inferred);
                    library = FsaSampleLibrary.Build(spec);
                }
                else
                {
                    library = FsaLibrary.BuildFromPeaks(
                        job.Peaks, job.Definitions, job.CrystalFractions, job.Options.AtomicXray);
                }

                if (library.Count == 0)
                {
                    message = Properties.Resources.FSANoComponents;
                }
                else
                {
                    computed = job.Analyzer.Analyze(job.Spectrum, job.Background, job.FwhmCalibration,
                                                    library, job.Efficiency);
                    if (computed == null)
                    {
                        message = Properties.Resources.FSANotPossible;
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("FSA failed: " + ex);
                // (`A95`) Тот же приём, что у перехвата подготовки: причина
                // называется, а не остаётся в журнале трассировки.
                message = FailureText(ex);
            }

            this.Finish(job, computed, message);
        }

        /// <summary>
        /// Публикация — или отказ от неё. Публикуется только счёт СВОЕГО
        /// поколения и только если за ним никто не стоит в очереди: иначе
        /// это промежуточный результат, и «актуальным» ему быть нельзя
        /// (критерий 10). Очередь запускается отсюда же, без потребителя.
        /// </summary>
        void Finish(Job job, FsaResult computed, string message)
        {
            bool idle;
            lock (this.sync)
            {
                this.running = false;
                this.activeStamp = null;
                Job next = this.pending;
                this.pending = null;

                if (next == null && job.Generation == this.generation)
                {
                    this.stamp = job.Stamp;
                    this.result = computed;
                    this.status = message;
                }
                // Иначе: сброс уже случился (считали прежний спектр) либо
                // снимок вытеснен более поздним — публиковать нечего.
                // Отпечаток остаётся прежним, и следующий проход подготовки
                // вида закажет счёт заново, если очередь пуста.

                if (next != null)
                {
                    this.Start(next);
                }

                idle = next == null;
            }

            if (idle)
            {
                EventHandler handler = this.Completed;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// (`A50`) Сказать о матрице ПРЕЖНЕГО ФОРМАТА — громче пометки в
        /// легенде и ровно один раз на файл.
        ///
        /// Приём тот же, каким приложение говорит о непрочитанной конфигурации
        /// прибора (<see cref="AppUi"/>): без окон — строка в поток ошибок,
        /// чтобы её видели пробы и корпусные прогоны; с окнами — сообщение,
        /// которое показывает ПОТРЕБИТЕЛЬ, забрав строку в
        /// <see cref="TakeResponseMatrixNotice"/> (окно посреди отрисовки
        /// открывать нельзя, см. там же).
        ///
        /// Ключ уже сказанного — путь, время записи и размер файла: пересчитали
        /// матрицу — скажем снова, а на каждый тик набора об одном и том же
        /// файле человек не услышит ничего.
        /// </summary>
        void NoteOldMatrixFormat(EfficiencyConfigData efficiency, int fileFormat)
        {
            string path = EfficiencyMaker.ResponseMatrixStore.PathOf(efficiency.Guid);
            string key;
            try
            {
                var file = new System.IO.FileInfo(path);
                key = path + ":" + file.LastWriteTimeUtc.Ticks + ":" + file.Length;
            }
            catch (Exception)
            {
                key = path;
            }

            string text = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                        Properties.Resources.FSAMatrixOldFormat,
                                        efficiency.Name, fileFormat,
                                        EfficiencyMaker.ResponseMatrix.FormatVersion,
                                        AppUi.Where(path));
            lock (this.sync)
            {
                if (key == this.matrixNoticeSaid)
                {
                    return;
                }

                this.matrixNoticeSaid = key;
                this.matrixNotice = text;
            }

            // Молчит, когда есть окна: там строку заберёт и покажет потребитель.
            AppUi.Note(text);
        }

        /// <summary>
        /// Минимальный снимок <see cref="ResultData"/>, который нужен выводу
        /// состава из баз. Полный <c>ResultData.Clone</c> здесь избыточен: FSA
        /// не читает импульсы, ROI и метаданные, а три используемых входа уже
        /// сняты до фоновой задачи.
        /// </summary>
        static ResultData CompositionInput(PeakDetectionMethodConfig peakConfig,
                                           EfficiencyConfigData efficiency,
                                           EnergySpectrum spectrum,
                                           FwhmCalibration fwhmCalibration)
        {
            return new ResultData
            {
                PeakDetectionMethodConfig = PeakConfigInput(peakConfig),
                Efficiency = efficiency,
                EnergySpectrum = spectrum,
                FwhmCalibration = fwhmCalibration
            };
        }

        /// <summary>
        /// Выводу состава от конфигурации поиска нужны только полоса и порог
        /// SNR. Остальные настройки остаются вне фонового снимка намеренно.
        /// </summary>
        static PeakDetectionMethodConfig PeakConfigInput(PeakDetectionMethodConfig source)
        {
            FWHMPeakDetectionMethodConfig fwhm = source as FWHMPeakDetectionMethodConfig;
            if (fwhm == null)
            {
                return null;
            }

            return new FWHMPeakDetectionMethodConfig
            {
                Min_Range = fwhm.Min_Range,
                Max_Range = fwhm.Max_Range,
                Min_SNR = fwhm.Min_SNR
            };
        }

        /// <summary>Отпечаток с настройками, снятыми с активной копии конфигурации спектра.</summary>
        public static string BuildStamp(ResultData resultData, bool subtractBackground)
        {
            return BuildStamp(resultData, subtractBackground, FsaCalculationOptions.Of(resultData));
        }

        /// <summary>
        /// ОТПЕЧАТОК ВХОДНЫХ ДАННЫХ: всё, от чего зависит разбор, и НИЧЕГО из
        /// того, что меняет только показ. Группировки родители/дочерние здесь
        /// нет и быть не должно (критерий 7); семь настроек расчёта входят
        /// строкой <see cref="FsaCalculationOptions.Stamp"/>.
        /// </summary>
        public static string BuildStamp(ResultData resultData, bool subtractBackground,
                                        FsaCalculationOptions options)
        {
            EnergySpectrum spectrum = resultData.EnergySpectrum;
            EnergySpectrum background = resultData.BackgroundEnergySpectrum;
            if (options == null)
            {
                options = FsaCalculationOptions.Of(resultData);
            }

            // Состав библиотеки задаётся найденными пиками, поэтому их набор
            // входит в отпечаток: сменился список пиков — разложение устарело.
            StringBuilder peakStamp = new StringBuilder();
            if (resultData.DetectedPeaks != null)
            {
                foreach (Peak peak in resultData.DetectedPeaks)
                {
                    if (peak != null && peak.Nuclide != null)
                    {
                        // (`A36`) В отпечаток идёт и ПОЛОЖЕНИЕ пика, а не только
                        // имя: на найденных пиках держится шкала модели, и два
                        // разбора с одинаковым списком имён, но разными
                        // центроидами — это два разных разбора.
                        peakStamp.Append(peak.Nuclide.Name).Append('@')
                                 .Append(peak.Energy.ToString("F1",
                                     System.Globalization.CultureInfo.InvariantCulture))
                                 .Append(';');
                    }
                }
            }

            return string.Concat(
                resultData.GetHashCode().ToString(),
                "|", spectrum.NumberOfChannels.ToString(),
                "|", spectrum.TotalPulseCount.ToString(),
                "|", spectrum.MeasurementTime.ToString("F1"),
                "|", subtractBackground ? "bg" : "nobg",
                "|", background != null ? background.TotalPulseCount.ToString() : "-",
                "|", EfficiencyStamp(resultData.Efficiency),
                "|", MatrixFileStamp(resultData.Efficiency),
                "|", CalibrationStamp(spectrum, resultData.FwhmCalibration),
                // Семь настроек расчёта (`A170`): источник состава (S57),
                // равновесие (S70) и пять компонентов модели. Каждая меняет
                // САМУ библиотеку или матрицу задачи, и без неё в отпечатке
                // на экране висело бы прежнее разложение.
                "|", options.Stamp,
                // (`A31`) Набор нуклидов — часть отпечатка. Состав библиотеки
                // идёт от ПОДПИСЕЙ пиков, а подписи ставит набор, и пока
                // менялись только его члены, отпечаток оставался прежним:
                // добавленный в набор нуклид на экране не появлялся, пока
                // человек не трогал что-нибудь ещё.
                "|", NuclideSetStamp(),
                "|", peakStamp.ToString());
        }

        /// <summary>
        /// Отпечаток активного набора: его имя, галка «прятать неопознанные» и
        /// СОСТАВ — число нуклидов, у которых этот набор отмечен. Правка
        /// членства меняет число, а переименование и смена набора — имя.
        ///
        /// ⚠ Считается перебором определений (полторы сотни записей), потому
        /// что членство хранится у НУКЛИДА (<see cref="NuclideDefinition.Sets"/>),
        /// а не у набора. Проход идёт на UI-потоке, там же, где снимаются
        /// остальные части отпечатка.
        /// </summary>
        static string NuclideSetStamp()
        {
            NuclideDefinitionManager manager = NuclideDefinitionManager.GetInstance();
            NuclideSet active = manager != null ? manager.ActiveSet : null;
            if (active == null)
            {
                return "all";
            }

            int members = 0;
            List<NuclideDefinition> definitions = manager.NuclideDefinitions;
            if (definitions != null)
            {
                foreach (NuclideDefinition definition in definitions)
                {
                    if (definition != null && definition.Sets != null
                        && definition.Sets.Contains(active.Id))
                    {
                        members++;
                    }
                }
            }

            return string.Concat(active.Name, ":", members.ToString(),
                                 active.HideUnknownPeaks ? ":hide" : "");
        }

        /// <summary>
        /// Файл матрицы отклика в отпечатке. Счёт решает «с матрицей или без»
        /// по файлу `.rmx` кривой — значит, устаревание обязано видеть его
        /// появление, пересчёт и удаление, иначе разложение «без матрицы»
        /// висит на экране и после того, как матрицу посчитали (и наоборот).
        /// Сам файл не читается — в отпечаток идут время записи и размер.
        /// </summary>
        static string MatrixFileStamp(EfficiencyConfigData efficiency)
        {
            if (efficiency == null || !efficiency.HasGeometry)
            {
                return "-";
            }

            // Выключатель (W11) — часть отпечатка: переключение галки обязано
            // устаревать готовое разложение, иначе «с матрицей» висит на
            // экране и после выключения (и наоборот).
            if (!efficiency.UseResponseMatrix)
            {
                return "off";
            }

            try
            {
                var file = new System.IO.FileInfo(
                    EfficiencyMaker.ResponseMatrixStore.PathOf(efficiency.Guid));
                return file.Exists
                    ? file.LastWriteTimeUtc.Ticks.ToString() + ":" + file.Length.ToString()
                    : "-";
            }
            catch (Exception)
            {
                // недоступный файл — то же, что отсутствующий: счёт его не прочтёт
                return "-";
            }
        }

        /// <summary>
        /// Кривая эффективности в отпечатке. Счёт берёт её из
        /// resultData.Efficiency, значит и устаревание обязано на неё смотреть:
        /// выбранная в панели измерения кривая раньше в отпечаток не входила, и
        /// разложение «без кривой» держалось на экране, пока не менялось
        /// что-нибудь постороннее.
        /// </summary>
        static string EfficiencyStamp(EfficiencyConfigData efficiency)
        {
            if (efficiency == null)
            {
                return "-";
            }

            return string.Concat(
                efficiency.Guid,
                ":", efficiency.LastUpdated.Ticks.ToString(),
                ":", efficiency.Curve != null ? efficiency.Curve.Count.ToString() : "0");
        }

        /// <summary>
        /// Обе калибровки в отпечатке — от них зависят и положения, и ширины
        /// линий образа. Энергетическая снимается пробами по трём каналам, а не
        /// коэффициентами: у неё несколько представлений (полином, нелинейная),
        /// и пробы покрывают любое.
        /// </summary>
        static string CalibrationStamp(EnergySpectrum spectrum, FwhmCalibration fwhmCalibration)
        {
            StringBuilder sb = new StringBuilder();
            EnergyCalibration energy = spectrum.EnergyCalibration;
            int channels = spectrum.NumberOfChannels;
            if (energy != null && channels > 0)
            {
                sb.Append(energy.ChannelToEnergy(0.0).ToString("R"));
                sb.Append(',').Append(energy.ChannelToEnergy(channels / 2.0).ToString("R"));
                sb.Append(',').Append(energy.ChannelToEnergy(channels - 1.0).ToString("R"));
            }

            double[] fwhm = fwhmCalibration != null ? fwhmCalibration.Coefficients : null;
            if (fwhm != null)
            {
                foreach (double coefficient in fwhm)
                {
                    sb.Append(';').Append(coefficient.ToString("R"));
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Мёртвое время прибора, снявшего спектр, — оно же ОКНО СОВПАДЕНИЯ
        /// каскадного суммирования (S27): длительность импульса и есть тот
        /// промежуток, внутри которого два кванта складываются в один отсчёт.
        /// Ноль — прибор его не назвал, суммирователь возьмёт своё умолчание.
        ///
        /// ⚠ Вызов обёрнут НЕ на всякий случай: `SerialInputDeviceConfig.DeadTime()`
        /// — заглушка декомпилятора и бросает `NotImplementedException`, а класс
        /// объявлен одним из вариантов `[XmlElement]` для `InputDeviceConfig`,
        /// то есть такая конфигурация читается штатно (TODO T48). Ронять из-за
        /// этого разложение нельзя: мёртвое время — уточнение поправки, а не
        /// условие её существования.
        /// </summary>
        static double DeadTimeOf(ResultData resultData)
        {
            try
            {
                if (resultData == null || resultData.DeviceConfig == null
                    || resultData.DeviceConfig.InputDeviceConfig == null)
                {
                    return 0.0;
                }

                double deadTime = resultData.DeviceConfig.InputDeviceConfig.DeadTime();
                return deadTime > 0.0 ? deadTime : 0.0;
            }
            catch (Exception ex)
            {
                Trace.WriteLine("FSA: мёртвое время недоступно, окно совпадения по умолчанию: " + ex.Message);
                return 0.0;
            }
        }
    }
}
