using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    /// <summary>
    /// (`AMBER208` (в), П236 06.10.2026) ПОЧЕМУ РАЗБОР ИДЁТ БЕЗ МАТРИЦЫ ОТКЛИКА —
    /// решение сеанса, названное словом. До этого дня отчёт писал «не учтена»,
    /// окно результата — «Бк по кривой», и у KCl в маринелли человек не видел,
    /// что матрица склада ЕСТЬ, но посчитана по геометрии кривой прибора, а
    /// разбор идёт по кривой из файла спектра от 14.08.2026 с другой геометрией.
    /// Лечение у каждой причины своё (посчитать / пересчитать / включить /
    /// выбрать кривую прибора), поэтому причина — перечислением, а не флагом.
    /// </summary>
    public enum FsaMatrixSkip
    {
        /// <summary>Матрица применена (или решение ещё не принималось).</summary>
        None,

        /// <summary>У кривой нет геометрии — матрицы быть не может, говорить не о чем.</summary>
        NoGeometry,

        /// <summary>Выключена галкой «Матрица отклика» в форме кривой (W11).</summary>
        SwitchedOff,

        /// <summary>Файла на складе нет — для этой геометрии не считали.</summary>
        NotComputed,

        /// <summary>Файл прежнего формата — пересчитать (`A50`).</summary>
        OldFormat,

        /// <summary>Файл не наш или оборван.</summary>
        Unreadable,

        /// <summary>
        /// Матрица есть и читается, но её клеймо не сходится с геометрией
        /// кривой разбора: геометрия или параметры менялись после расчёта,
        /// либо кривая в файле спектра старше кривой прибора.
        /// </summary>
        StaleGeometry
    }

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

        bool matrixFromSpectrum;

        FsaMatrixSkip matrixSkip;

        string spectrumMatrixRefusal;

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

        /// <summary>
        /// ⚠ ПРОТОТИП ПОЛОСЫ П236 (06.10.2026): крючок проб для развёрток
        /// внутренних ключей разбора (веса, Хубер, шум) на ТОМ ЖЕ сеансе, что
        /// у окна. Ставится только пробой (`FsaBqProbe`); в приложении — null.
        /// </summary>
        public static Action<FsaAnalyzer> ProbeAnalyzerHook = null;

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
        /// (`AMBER202`) Матрица разбора взята из ФАЙЛА СПЕКТРА, а не со склада:
        /// окно отчёта говорит об этом в строке «Матрица отклика».
        /// </summary>
        public bool ResponseMatrixFromSpectrum
        {
            get
            {
                lock (this.sync)
                {
                    return this.matrixFromSpectrum;
                }
            }
        }

        /// <summary>
        /// (`AMBER208` (в)) Почему разбор идёт без матрицы отклика — для окна
        /// результата и отчёта; <see cref="FsaMatrixSkip.None"/> — применена.
        /// </summary>
        public FsaMatrixSkip ResponseMatrixSkip
        {
            get
            {
                lock (this.sync)
                {
                    return this.matrixSkip;
                }
            }
        }

        /// <summary>(`AMBER208` (в)) Отказ чтения склада — причиной для человека.</summary>
        static FsaMatrixSkip SkipOf(EfficiencyMaker.MatrixRefusal refusal)
        {
            switch (refusal)
            {
                case EfficiencyMaker.MatrixRefusal.OldFormat:
                    return FsaMatrixSkip.OldFormat;
                case EfficiencyMaker.MatrixRefusal.NotOurs:
                case EfficiencyMaker.MatrixRefusal.Unreadable:
                    return FsaMatrixSkip.Unreadable;
                default:
                    return FsaMatrixSkip.NotComputed;
            }
        }

        /// <summary>
        /// (`AMBER202`) Почему матрица из файла спектра НЕ взята — словами;
        /// пусто — её не было или она взята.
        /// </summary>
        public string SpectrumMatrixRefusal
        {
            get
            {
                lock (this.sync)
                {
                    return this.spectrumMatrixRefusal ?? "";
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
        /// (`AMBER201`, мелочь 6.9, 05.10.2026) Пересчёт НАЧАЛСЯ — потребителям пора
        /// показать «идёт расчёт». Без него окно отчёта узнавало о счёте только по
        /// <see cref="Completed"/>, и всё время пересчёта строка состояния стояла
        /// зелёным «готово» над прежними числами. Приходит с потока, позвавшего
        /// <see cref="EnsureUpToDate(ResultData, bool, FsaCalculationOptions)"/>
        /// (вид спектра — поток окон), вне замка; на постановку в очередь при
        /// уже идущем счёте не приходит — «идёт расчёт» уже показано.
        /// </summary>
        public event EventHandler Started;

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

            EventHandler started = this.Started;
            if (started != null)
            {
                started(this, EventArgs.Empty);
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
                          + string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                          Properties.Resources.ERRFailureReason,
                                          AppUi.Reason(ex));
            AppUi.Note(text);
            return text;
        }

        /// <summary>
        /// (`A312`) СЛОВА ОТКАЗА <see cref="FsaAnalyzer.Analyze"/> — по причине
        /// <see cref="FsaAnalyzer.Refusal"/>, на языке окна (обе культуры в
        /// `Resources.resx` / `Resources.ru.resx`).
        ///
        /// До 21.09.2026 слова были у ОДНОЙ причины из семи (геометрия,
        /// `A277`), остальные шесть сливались в `FSANotPossible`, и на спектре
        /// Amber `Am-241` (состав из NucBase — один образ рентгена кристалла,
        /// который матрица отклика снимает по `AMBER4`) человек читал
        /// «разложение невозможно», хотя лечится это одним нуклидом в составе.
        ///
        /// ⛔ Имён нуклидов и образов здесь нет и быть не должно: причина
        /// называет РОД лекарства (состав, полоса, калибровка), а не то, что
        /// именно снято, — подробность с числами уходит в `Trace`
        /// (<see cref="FsaAnalyzer.RefusalNote"/>). Числа порогов приходят
        /// аргументами из констант анализатора, копии в ресурсах нет.
        ///
        /// (`AMBER34`) Кривая выбрана и ОТВЕРГНУТА (точка выше единицы у долей):
        /// гейт геометрии сработал от `efficiency == null`, но чинить надо
        /// точку, а не геометрию, — называется причина кривой.
        /// `FSANotPossible` остаётся ТОЛЬКО на причину, которой этот список не
        /// знает (новый член перечисления без слов), — и это заметно.
        ///
        /// Открыт (а не <c>static</c> внутри) ради приёмки: `FsaReportViewProbe`
        /// (раздел 14) судит, что у КАЖДОГО члена перечисления есть свои слова
        /// в обеих культурах и ни один не падает в `FSANotPossible`.
        /// </summary>
        public static string RefusalText(FsaRefusal refusal, string efficiencyRefusal)
        {
            switch (refusal)
            {
                case FsaRefusal.Geometry:
                    return efficiencyRefusal != null
                        ? string.Format(CultureInfo.InvariantCulture,
                                        Properties.Resources.FSACurveRefused, efficiencyRefusal)
                        : Properties.Resources.FSANoGeometry;
                case FsaRefusal.LibraryEmptied:
                    return Properties.Resources.FSALibraryEmptied;
                case FsaRefusal.NarrowBand:
                    return string.Format(CultureInfo.InvariantCulture,
                                         Properties.Resources.FSABandTooNarrow, FsaAnalyzer.MinBandChannels);
                case FsaRefusal.FewChannels:
                    return string.Format(CultureInfo.InvariantCulture,
                                         Properties.Resources.FSATooFewChannels, FsaAnalyzer.MinChannels);
                case FsaRefusal.NoFit:
                    return Properties.Resources.FSANoFit;
                case FsaRefusal.Input:
                    return Properties.Resources.FSANoCalibration;
                case FsaRefusal.NoLiveTime:
                    return Properties.Resources.FSANoLiveTime;
                default:
                    return Properties.Resources.FSANotPossible;
            }
        }

        /// <summary>
        /// (`A205`) НАСКОЛЬКО СИЛЬНЕЙШИЙ КАНДИДАТ НЕ ДОТЯНУЛ — приписка к «нет
        /// компонентов», без которой пустой разбор молчит о причине.
        ///
        /// Выходит «(Th-232 23 % &lt; 30 %)»: имя нуклида, его доля и порог. Ни
        /// одного переводимого слова здесь нет НАРОЧНО — новая строка ресурса
        /// потребовала бы правки обоих `Resources.resx`, а причина нужна на
        /// обоих языках одинаково. Числа печатаются инвариантной культурой
        /// (`A242`), группировки разрядов нет (`A244`).
        ///
        /// ⚠ Пусто — сказать нечего: кандидатов не было вовсе, и это ДРУГОЙ
        /// случай, чем «кандидат был и не дотянул». Молчать о нём честнее, чем
        /// печатать ноль как долю.
        /// </summary>
        static string Shortfall(FsaCompositionInference.Report report)
        {
            if (report == null || report.Candidates.Count == 0)
            {
                return null;
            }

            FsaParentEvidence best = null;
            double top = -1.0;
            foreach (FsaParentEvidence candidate in report.Candidates)
            {
                double share = double.IsNaN(candidate.HeadCoverage)
                    ? candidate.Coverage
                    : Math.Max(candidate.Coverage, candidate.HeadCoverage);
                if (best == null || share > top)
                {
                    best = candidate;
                    top = share;
                }
            }

            return string.Format(CultureInfo.InvariantCulture, "({0} {1} % < {2} %)",
                                 best.Name,
                                 (100.0 * top).ToString("F0", CultureInfo.InvariantCulture),
                                 (100.0 * report.Coverage).ToString("F0", CultureInfo.InvariantCulture));
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

            /// <summary>
            /// (`AMBER34`) Кривая выбрана, но ОТВЕРГНУТА с причиной
            /// (<see cref="FsaEfficiency.FromConfig(EfficiencyConfigData, out string)"/>);
            /// null — кривая есть либо не выбрана. Без этого отказ гейта
            /// геометрии называл бы геометрию там, где чинить надо точку.
            /// </summary>
            public string EfficiencyRefusal;
            public ResultData CompositionInput;
            public List<NuclideDefinition> Definitions;
            public List<Peak> Peaks;
            /// <summary>(П236) Активный сет на момент постановки — источник состава «из сета».</summary>
            public NuclideSet Set;
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
            // (`AMBER34`) Кривая сцены поля (см²) доезжает до разбора КАК ФОРМА
            // — `FromConfig` не отвечает на неё null (иначе гейт геометрии
            // отказал бы разбору целиком, вопреки решению Amber 15.09.2026
            // «Разбор идёт, Бк скрыты с причиной»); признак нормировки несёт
            // сама кривая, читает его `FsaAnalyzer` → `FsaResult` → окно отчёта.
            // Отвергнутая кривая (точка выше единицы у долей) — причина словами.
            job.Efficiency = FsaEfficiency.FromConfig(efficiencyConfig, out job.EfficiencyRefusal);
            job.CompositionInput = CompositionInput(resultData.PeakDetectionMethodConfig,
                                                    efficiencyConfig, job.Spectrum, job.FwhmCalibration,
                                                    resultData.DeviceConfig);

            // Снимок списков: их правит UI-поток (конструктор сетов, NucBase),
            // а перечисление живого списка в фоне ловит «Collection was modified».
            NuclideDefinitionManager nuclideManager = NuclideDefinitionManager.GetInstance();
            job.Definitions = new List<NuclideDefinition>(nuclideManager.NuclideDefinitions);
            job.Set = nuclideManager.ActiveSet;
            job.Peaks = resultData.DetectedPeaks != null
                ? new List<Peak>(resultData.DetectedPeaks)
                : new List<Peak>();

            FsaAnalyzer analyzer = new FsaAnalyzer();

            // (`A170`) Пользовательские настройки — во внутренние ключи ОДНИМ
            // фасадом; `BackscatterWithMatrix` при этом опускается, `EscapeGate`
            // не трогается.
            options.ApplyTo(analyzer);

            Action<FsaAnalyzer> probeHook = ProbeAnalyzerHook;
            if (probeHook != null)
            {
                probeHook(analyzer);
            }

            // (`AMBER155` (в), П220) свойства прибора (кривизна тракта) — тем же
            // местом, что у проб, снимком на UI-потоке
            analyzer.AdoptDevice(resultData.DeviceConfig);

            // Матрица отклика берётся у ТОЙ ЖЕ кривой, что и эффективность, и
            // только если её отпечаток сходится с нынешней геометрией. Не
            // сошёлся — работаем без неё, старым путём: посчитать спектр по
            // матрице чужой геометрии хуже, чем не посчитать вовсе.
            // UseResponseMatrix — выключатель пользователя (W11, галка в форме
            // «Матрица отклика»): выключено — считаем без матрицы, файл даже
            // не читаем.
            bool oldFormat = false;
            bool fromSpectrum = false;
            string spectrumRefusal = "";
            FsaMatrixSkip skip = FsaMatrixSkip.None;
            if (efficiencyConfig != null && efficiencyConfig.HasGeometry
                && efficiencyConfig.UseResponseMatrix)
            {
                // ⛔ (`A50`) ОТКАЗ ЧИТАТЬ МАТРИЦУ ОБЯЗАН НАЗЫВАТЬ СЕБЯ. Прежде
                // `Load` возвращал `null` молча — и «файла нет» было
                // неотличимо от «файл есть, но посчитан прежним форматом»: в
                // легенде обоим доставалась одна пометка «· без матрицы».
                // Лечится это по-разному (посчитать против пересчитать), и
                // сказать человеку, что именно с ним случилось, было нечем.
                //
                // (`AMBER202`) Матрица — ОБЩИМ путём читателей: склад, а нет на
                // нём годной — приехавшая в файле спектра (в памяти). Отказ
                // встроенной называет себя строкой окна отчёта.
                EfficiencyMaker.MatrixRefusal refusal;
                int fileFormat;
                EfficiencyMaker.ResponseMatrixSource source;
                EfficiencyMaker.ResponseMatrix matrix =
                    EfficiencyMaker.ResponseMatrixStore.Resolve(efficiencyConfig, out refusal, out fileFormat,
                                                                out source, out spectrumRefusal);
                fromSpectrum = source == EfficiencyMaker.ResponseMatrixSource.Spectrum;
                if (refusal == EfficiencyMaker.MatrixRefusal.OldFormat)
                {
                    oldFormat = true;
                    this.NoteOldMatrixFormat(efficiencyConfig, fileFormat);
                }

                if (matrix != null && matrix.IsValidFor(efficiencyConfig.Geometry))
                {
                    // ⛔ Матрица кладётся ОДНИМ движением и общим кодом
                    // (`FsaMatrixBinding`, `AMBER12`): вместе с нею едут вещество
                    // кристалла (`S20`) и признак защиты. Пробы зовут его же —
                    // иначе стенд и экран расходятся молча, а снимок «до/после»
                    // выходит побитово одинаковым.
                    FsaMatrixBinding.Bind(analyzer, efficiencyConfig.Geometry, matrix);
                }
                else
                {
                    // (`AMBER208` (в)) матрица прочиталась, а клеймо не сошлось —
                    // «посчитана для другой геометрии»; не прочиталась — отказ
                    // склада словом (нет файла / прежний формат / не читается)
                    skip = matrix != null ? FsaMatrixSkip.StaleGeometry : SkipOf(refusal);
                }
            }
            else
            {
                skip = efficiencyConfig != null && efficiencyConfig.HasGeometry
                    ? FsaMatrixSkip.SwitchedOff
                    : FsaMatrixSkip.NoGeometry;
            }

            // Решение о матрице — сразу, как и прежде: о нём спрашивают и до
            // того, как счёт вернулся (`ResponseMatrixFormProbe`).
            lock (this.sync)
            {
                this.matrixOldFormat = oldFormat;
                this.matrixFromSpectrum = fromSpectrum;
                this.spectrumMatrixRefusal = spectrumRefusal;
                this.matrixSkip = skip;
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
            else if (resultData.DeviceConfig != null)
            {
                // ⛔ `A276`: путь ПО ПИКАМ — умолчание разбора
                // (`DbLookupsForFsa` выключена), и без этой ветки поле прибора
                // молчало бы у большинства людей: доли получал бы только тот,
                // кто включил вывод состава из баз. Порядок тот же, что у
                // второго пути, и он виден прямо здесь: ветка ЗАПАСНАЯ — при
                // живой геометрии сюда не попадают вовсе.
                Dictionary<int, double> named = FsaSampleLibrary.FractionsOfMaterial(
                    resultData.DeviceConfig.CrystalMaterialName);
                if (named.Count > 0)
                {
                    job.CrystalFractions = named;
                }
            }

            return job;
        }

        /// <summary>Фоновая половина: библиотека, разбор, публикация.</summary>
        void Compute(Job job)
        {
            FsaResult computed = null;
            string message = null;

            // ⛔ (`AMBER199`, 05.10.2026) Собиратель отказов чтения баз — на весь
            // фоновый счёт: сборка библиотеки, разбор, расхождения поставок.
            // Отказ НЕ кэшируется читателями (следующий пересчёт спросит базу
            // снова) и доходит до человека: строкой окна отчёта при результате,
            // припиской к причине — без него.
            FsaDatabaseFailures.Begin();
            List<string> databaseFailures;
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
                // (`A205`) Чем кончился вывод состава — человеку, а не только в
                // журнал трассировки: пустой разбор при источнике «Из NucBase»
                // до 06.09.2026 не называл ни одной причины.
                string shortfall = null;
                List<FsaSampleChain> setChains = null;
                List<string> setNuclides = null;
                if (job.Options.FromSet)
                {
                    // (П236; решение Amber 06.10.2026 «Из сета нуклидов») Состав —
                    // родители активного сета, как объявленный состав корпусных
                    // проб (`--chain=`/`--sample=`): ряд по метке `Chain`, одиночка
                    // по имени. Пики не читаются: нуклид сета без пика остаётся
                    // кандидатом и получает предел обнаружения.
                    FsaMeasurementResult.DeclaredOf(job.Set, job.Definitions, out setChains, out setNuclides);
                }
                if (setChains != null && (setChains.Count > 0 || setNuclides.Count > 0))
                {
                    FsaSampleSpec spec = FsaSampleSpec.Declared(job.CompositionInput, setChains, setNuclides,
                                                                job.Options.ChainEquilibrium, job.Options.AtomicXray);
                    job.Options.ApplyTo(spec);
                    Trace.WriteLine("FSA composition: nuclide set, chains " + setChains.Count
                                    + ", nuclides " + setNuclides.Count);
                    library = FsaSampleLibrary.Build(spec);
                }
                else if (job.Options.DbLookups)
                {
                    FsaCompositionInference.Report inferred;
                    FsaSampleSpec spec = FsaCompositionInference.Infer(job.Peaks, job.CompositionInput, out inferred);
                    job.Options.ApplyTo(spec);
                    Trace.WriteLine("FSA composition: " + inferred);
                    shortfall = Shortfall(inferred);
                    library = FsaSampleLibrary.Build(spec);
                }
                else
                {
                    library = FsaLibrary.BuildFromPeaks(
                        job.Peaks, job.Definitions, job.CrystalFractions, job.Options.AtomicXray);
                }

                if (library.Count == 0)
                {
                    message = Properties.Resources.FSANoComponents
                              + (shortfall != null ? " " + shortfall : "");
                }
                else
                {
                    computed = job.Analyzer.Analyze(job.Spectrum, job.Background, job.FwhmCalibration,
                                                    library, job.Efficiency);
                    if (computed != null)
                    {
                        // (`S187`) Расхождения поставок у нуклидов состава — здесь,
                        // в фоне: чтение базы не должно идти в окне отчёта.
                        computed.SupplyDiscrepancies = FsaCascadeSummer.SupplyDiscrepanciesOf(computed);
                    }

                    if (computed == null)
                    {
                        // ⛔ (`A277`, `A312`) ОТКАЗ НАЗЫВАЕТ СЕБЯ. Гаснущий
                        // экран без слов — это ровно «признак без читателя»:
                        // человек видит пустоту и не знает, что лечится она
                        // одним описанием кристалла в редакторе геометрии —
                        // или одним нуклидом в составе. Решение о самом
                        // отказе принято ОДНИМ местом (`FsaAnalyzer.Refuse`,
                        // причина — `FsaAnalyzer.Refusal`), здесь только слова.
                        message = RefusalText(job.Analyzer.Refusal, job.EfficiencyRefusal);
                        Trace.WriteLine("FSA refused: " + job.Analyzer.Refusal
                                        + (job.Analyzer.RefusalNote != null ? " — " + job.Analyzer.RefusalNote : ""));
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
            finally
            {
                databaseFailures = FsaDatabaseFailures.End();
            }

            if (computed != null)
            {
                computed.DatabaseFailures = databaseFailures;
            }
            else if (databaseFailures.Count > 0 && message != null)
            {
                message += " " + DatabaseFailureText(databaseFailures);
            }

            this.Finish(job, computed, message);
        }

        /// <summary>
        /// (`AMBER199`) Слова об отказе чтения баз: подпись (обе культуры) и
        /// первая строка отказа; остальные — числом. Строки отказа — имя файла,
        /// ключ и сообщение платформы, переводу не подлежат.
        /// </summary>
        public static string DatabaseFailureText(List<string> failures)
        {
            if (failures == null || failures.Count == 0)
            {
                return string.Empty;
            }

            string text = string.Format(CultureInfo.InvariantCulture,
                                        Properties.Resources.FSADatabaseFailed, failures[0]);
            if (failures.Count > 1)
            {
                text += string.Format(CultureInfo.InvariantCulture, " (+{0})", failures.Count - 1);
            }

            return text;
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

            string text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
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
        ///
        /// (`AMBER180`, 05.10.2026) Прибор едет сюда тоже — ради ОДНОГО поля,
        /// вещества кристалла (`FsaSampleSpec.OfSpectrum` → `CrystalMaterialName`,
        /// второй источник долей по `A276`). До этого дня снимок его не нёс, и
        /// на пути «из баз» у кривой без геометрии отбор родителей образов
        /// вылета оставался без долей кристалла, хотя прибор их называл.
        /// Снимком служит минимальная копия с одним этим полем: прибор правит
        /// UI-поток, а читается поле уже в фоне.
        /// </summary>
        static ResultData CompositionInput(PeakDetectionMethodConfig peakConfig,
                                           EfficiencyConfigData efficiency,
                                           EnergySpectrum spectrum,
                                           FwhmCalibration fwhmCalibration,
                                           DeviceConfigInfo device)
        {
            return new ResultData
            {
                PeakDetectionMethodConfig = PeakConfigInput(peakConfig),
                Efficiency = efficiency,
                EnergySpectrum = spectrum,
                FwhmCalibration = fwhmCalibration,
                DeviceConfig = DeviceInput(device)
            };
        }

        /// <summary>
        /// (`AMBER180`) Минимальный снимок прибора для вывода состава: только
        /// вещество кристалла. Полная копия (<c>new DeviceConfigInfo(info)</c>)
        /// клонирует тракт ввода и калибровки, которые выводу не нужны.
        /// </summary>
        static DeviceConfigInfo DeviceInput(DeviceConfigInfo device)
        {
            if (device == null || string.IsNullOrEmpty(device.CrystalMaterialName))
            {
                return null;
            }

            return new DeviceConfigInfo { CrystalMaterialName = device.CrystalMaterialName };
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

            // ⛔ (`A244`) Числа отпечатка печатаются ИНВАРИАНТОМ, а не культурой
            // потока. Отпечаток снимается и с UI-потока, и из фоновой задачи, а
            // культуру с подменённым разделителем ставит себе только первый:
            // «12,5» из задачи и «12.5» с формы — это два РАЗНЫХ отпечатка у
            // одного и того же спектра, то есть вечное «устарело».
            return string.Concat(
                resultData.GetHashCode().ToString(CultureInfo.InvariantCulture),
                "|", spectrum.NumberOfChannels.ToString(CultureInfo.InvariantCulture),
                "|", spectrum.TotalPulseCount.ToString(CultureInfo.InvariantCulture),
                "|", spectrum.MeasurementTime.ToString("F1", CultureInfo.InvariantCulture),
                // ⛔ (`AMBER180`, 05.10.2026) ЖИВОЕ ВРЕМЯ — в отпечатке. Разбор
                // делит на `EffectiveLiveTime` и по `LiveTime` выбирает убыль
                // наложений (`FsaAnalyzer.PileUpLossFor`, `PileUpCapFor`), а
                // «Применить поправку на мёртвое время» меняет ТОЛЬКО его: без
                // этой части отчёт FSA показывал прежние Бк, а панель выделения
                // и ось имп/с — новые. "R" — поправка бывает меньше десятой
                // доли секунды, и округление её съело бы.
                "|", spectrum.LiveTime.ToString("R", CultureInfo.InvariantCulture),
                "|", subtractBackground ? "bg" : "nobg",
                "|", BackgroundStamp(background),
                "|", EfficiencyStamp(resultData.Efficiency),
                "|", MatrixFileStamp(resultData.Efficiency),
                "|", CalibrationStamp(spectrum, resultData.FwhmCalibration),
                // (`AMBER180`) Полоса и порог поиска: `Min_Range`/`Max_Range`
                // уходят в `analyzer.MinEnergy/MaxEnergy` и в спецификацию
                // состава (`FsaSampleSpec.OfSpectrum`), `Min_SNR` — в порог
                // ожидаемых линий вывода состава (`FsaCompositionInference.Score`)
                // прямо, а не только через список пиков.
                "|", PeakConfigStamp(resultData.PeakDetectionMethodConfig),
                // (`AMBER180`) Прибор: кривизна тракта (`AdoptDevice`), мёртвое
                // время — окно совпадения (`CoincidenceWindowSec`) — и вещество
                // кристалла (запасной источник долей без геометрии, `A276`).
                "|", DeviceStamp(resultData),
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
            List<NuclideDefinition> definitions = manager != null ? manager.NuclideDefinitions : null;
            if (active == null)
            {
                // (`AMBER180`) и без набора линии образа берутся из определений
                return "all:" + DefinitionsHash(definitions).ToString(CultureInfo.InvariantCulture);
            }

            int members = 0;
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

            return string.Concat(active.Name, ":",
                                 members.ToString(CultureInfo.InvariantCulture),
                                 active.HideUnknownPeaks ? ":hide" : "",
                                 ":", DefinitionsHash(definitions).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER180`, 05.10.2026) СОДЕРЖАНИЕ ОПРЕДЕЛЕНИЙ НУКЛИДОВ — свёрткой.
        /// Путь по пикам (<c>FsaLibrary.BuildFromPeaks</c>)
        /// берёт линии образа из ВСЕХ определений — энергию и выход, — и правка
        /// выхода линии в редакторе нуклидов отпечатка не меняла: разложение
        /// со старым выходом держалось на экране. Свёртка — внутри процесса
        /// (хэши строк в отпечатке живут столько же, сколько он сам).
        /// </summary>
        static int DefinitionsHash(List<NuclideDefinition> definitions)
        {
            int hash = 17;
            if (definitions == null)
            {
                return hash;
            }

            unchecked
            {
                foreach (NuclideDefinition definition in definitions)
                {
                    if (definition == null)
                    {
                        hash = hash * 31;
                        continue;
                    }

                    hash = hash * 31 + (definition.Name != null ? definition.Name.GetHashCode() : 0);
                    hash = hash * 31 + definition.Energy.GetHashCode();
                    hash = hash * 31 + definition.Intencity.GetHashCode();
                }
            }

            return hash;
        }

        /// <summary>
        /// (`AMBER180`) Фон в отпечатке: его отсчёты, число каналов (не то —
        /// фон отвергается), живое и полное время (знаменатель нормировки фона,
        /// `backgroundScale = liveTime / backgroundLive`) и его собственная
        /// шкала — по ней фон перекладывается в шкалу пробы
        /// (<c>RebinBackgroundToSpectrum</c>). Прежде здесь было одно число
        /// отсчётов.
        /// </summary>
        static string BackgroundStamp(EnergySpectrum background)
        {
            if (background == null)
            {
                return "-";
            }

            return string.Concat(
                background.TotalPulseCount.ToString(CultureInfo.InvariantCulture),
                ":", background.NumberOfChannels.ToString(CultureInfo.InvariantCulture),
                ":", background.LiveTime.ToString("R", CultureInfo.InvariantCulture),
                ":", background.MeasurementTime.ToString("R", CultureInfo.InvariantCulture),
                ":", CalibrationStamp(background, null));
        }

        /// <summary>(`AMBER180`) Полоса и порог поиска пиков — входы разбора и вывода состава.</summary>
        static string PeakConfigStamp(PeakDetectionMethodConfig config)
        {
            FWHMPeakDetectionMethodConfig fwhm = config as FWHMPeakDetectionMethodConfig;
            if (fwhm == null)
            {
                return "-";
            }

            return string.Concat(
                fwhm.Min_Range.ToString("R", CultureInfo.InvariantCulture),
                ":", fwhm.Max_Range.ToString("R", CultureInfo.InvariantCulture),
                ":", fwhm.Min_SNR.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// (`AMBER180`) Свойства прибора, которые снимок передаёт разбору:
        /// кривизна тракта, мёртвое время (окно совпадения) и вещество
        /// кристалла. Мёртвое время берётся тем же <see cref="DeadTimeOf"/>,
        /// что и снимок, — с его же перехватом заглушки.
        /// </summary>
        static string DeviceStamp(ResultData resultData)
        {
            DeviceConfigInfo device = resultData.DeviceConfig;
            if (device == null)
            {
                return "-";
            }

            return string.Concat(
                device.TractCurvature.ToString("R", CultureInfo.InvariantCulture),
                ":", DeadTimeOf(resultData).ToString("R", CultureInfo.InvariantCulture),
                ":", device.CrystalMaterialName ?? string.Empty);
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

            // (`AMBER202`) отметка ОБОИХ источников матрицы — склада и блока из
            // файла спектра — общим местом читателей
            return EfficiencyMaker.ResponseMatrixStore.SourceStamp(efficiency);
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
                ":", efficiency.LastUpdated.Ticks.ToString(CultureInfo.InvariantCulture),
                ":", efficiency.Curve != null
                         ? efficiency.Curve.Count.ToString(CultureInfo.InvariantCulture)
                         : "0");
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
                sb.Append(energy.ChannelToEnergy(0.0).ToString("R", CultureInfo.InvariantCulture));
                sb.Append(',').Append(energy.ChannelToEnergy(channels / 2.0)
                                            .ToString("R", CultureInfo.InvariantCulture));
                sb.Append(',').Append(energy.ChannelToEnergy(channels - 1.0)
                                            .ToString("R", CultureInfo.InvariantCulture));
            }

            double[] fwhm = fwhmCalibration != null ? fwhmCalibration.Coefficients : null;
            if (fwhm != null)
            {
                foreach (double coefficient in fwhm)
                {
                    sb.Append(';').Append(coefficient.ToString("R", CultureInfo.InvariantCulture));
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

        /// <summary>(П195) Умолчание карточки пробы — вес, кг (`SampleInfoData`).</summary>
        public const double DefaultCardWeightKg = 1.0;

        /// <summary>(П195) Умолчание карточки пробы — объём, л (`SampleInfoData`).</summary>
        public const double DefaultCardVolumeL = 1.0;

        /// <summary>
        /// (`AMBER159`, П195 01.10.2026; решение Amber «Предупреждение с
        /// величиной») ПЛОТНОСТЬ ПРОБЫ ПРОТИВ ПЛОТНОСТИ СЦЕНЫ. Разбор считает
        /// активность матрицей (и кривой из геометрии) с плотностью вещества
        /// источника сцены (<see cref="EfficiencyMaker.GeometryModel.Source"/>),
        /// а вес и объём пробы человек вписывает в карточку
        /// (<see cref="SampleInfoData.Weight"/>, кг; <see cref="SampleInfoData.Volume"/>,
        /// л — единицы хранения, `DCSampleInfoView`), и до 01.10.2026 их никто
        /// не сверял: проба 1.3 г/см³ в маринелли со сценой 0.66 давала
        /// активность ниже на 15 % при 60 кэВ и на 7 % при 662 — молча.
        ///
        /// Поправка активности НЕ делается (решение Amber: делается только
        /// сверка); здесь — расхождение и оценка цены. null — сверять нечего:
        /// нет веса или объёма (нуль — «не вписан»), нет геометрии, у сцены нет
        /// пробы (точечный источник).
        ///
        /// Зовёт окно отчёта при каждом заполнении, а не сеанс при счёте:
        /// вес и объём в отпечаток разбора не входят (на разложение они не
        /// влияют), и строка, посчитанная при счёте, осталась бы старой после
        /// правки карточки.
        ///
        /// ⚠ Цена — ОЦЕНКА, названная так и в строке окна: наклон ln ε по
        /// плотности из прямого счёта П191 на маринелли 1 л
        /// (`Nano16Pro_Marinelli.in`, ρ 0.66 / 1.3, 3 М историй на узел, шум
        /// узла 1.3…1.8 %): ε(0.66)/ε(1.3) = 1.177 при 60 кэВ и 1.078 при 662 —
        /// у другого сосуда и другой высоты пробы наклон другой.
        /// </summary>
        public static FsaSampleDensityCheck CheckSampleDensity(SampleInfoData sample,
                                                               EfficiencyMaker.GeometryModel geometry)
        {
            if (sample == null || geometry == null || geometry.Source == null
                || !geometry.HasSampleVolume
                || !(sample.Weight > 0.0) || !(sample.Volume > 0.0)
                || double.IsInfinity(sample.Weight) || double.IsInfinity(sample.Volume)
                || !(geometry.Source.Density > 0.0))
            {
                return null;
            }

            // ⚠ (П195) 1 кг и 1 л РОВНО — умолчание карточки (`SampleInfoData`:
            // `weight = 1.0`, `volume = 1.0`), а не вписанные числа: так стоит у
            // 112 из 131 спектра корпуса и у спектров витрины Amber. Посылка
            // решения «нуль — не вписан» уже реальности; сверять умолчание значило
            // бы красить «+52 %» почти каждое измерение в маринелли ОИСН 0.66.
            // Цена — настоящая проба 1 кг в 1 л не сверяется; выбор — за Amber.
            if (sample.Weight == DefaultCardWeightKg && sample.Volume == DefaultCardVolumeL)
            {
                return null;
            }

            // кг/л = г/см³
            double sampleDensity = sample.Weight / sample.Volume;
            double sceneDensity = geometry.Source.Density;
            double delta = sampleDensity - sceneDensity;
            return new FsaSampleDensityCheck
            {
                SampleDensity = sampleDensity,
                SceneDensity = sceneDensity,
                DeviationPercent = 100.0 * (sampleDensity / sceneDensity - 1.0),
                Efficiency60Percent = 100.0 * (Math.Exp(-FsaSampleDensityCheck.LogSlope60 * delta) - 1.0),
                Efficiency662Percent = 100.0 * (Math.Exp(-FsaSampleDensityCheck.LogSlope662 * delta) - 1.0)
            };
        }
    }

    /// <summary>
    /// (`AMBER159`, П195) Итог сверки плотности пробы с плотностью сцены —
    /// <see cref="FsaAnalysisSession.CheckSampleDensity"/>; читает окно отчёта.
    /// </summary>
    public sealed class FsaSampleDensityCheck
    {
        /// <summary>
        /// Наклон −d ln ε / dρ пика при 60 кэВ, см³/г: ln(0.01705/0.01449)/0.64
        /// по прямому счёту П191 (маринелли 1 л, ρ 0.66 против 1.3).
        /// </summary>
        public const double LogSlope60 = 0.2542;

        /// <summary>То же при 662 кэВ: ln(0.00371/0.00344)/0.64.</summary>
        public const double LogSlope662 = 0.1181;

        /// <summary>
        /// Порог предупреждения по |расхождению плотностей|, % — из решения
        /// Amber 01.10.2026 («при |Z| &gt; 10 % — предупреждение в окне отчёта»).
        /// </summary>
        public const double WarningPercent = 10.0;

        /// <summary>Плотность пробы W/V, г/см³.</summary>
        public double SampleDensity { get; set; }

        /// <summary>Плотность вещества источника сцены, г/см³.</summary>
        public double SceneDensity { get; set; }

        /// <summary>(ρ пробы / ρ сцены − 1) · 100, %.</summary>
        public double DeviationPercent { get; set; }

        /// <summary>
        /// Оценка ε пробы против ε сцены при 60 кэВ, % (знак: минус — у пробы
        /// эффективность ниже, активность разбора занижена так же).
        /// </summary>
        public double Efficiency60Percent { get; set; }

        /// <summary>То же при 662 кэВ, %.</summary>
        public double Efficiency662Percent { get; set; }

        /// <summary>|расхождение| выше порога — строка окна красная.</summary>
        public bool Warning
        {
            get { return Math.Abs(this.DeviationPercent) > WarningPercent; }
        }
    }
}
