using BecquerelMonitor.FullSpectrumAnalysis;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    /// <summary>
    /// Отрисовка полноспектрального разложения (BackgroundMode.ShowFSA).
    ///
    /// Спектр показывается послойным стеком: снизу вверх идут вклады
    /// компонентов с разнесённой по ним подложкой континуума, верх стека — сумма
    /// модели, поверх линией — измеренный спектр за вычетом фона. Так читается
    /// сразу и состав («пирог» в легенде), и качество описания: там, где линия
    /// спектра отрывается от верха стека, у модели нет образа.
    ///
    /// (`A145`, этап 2) Вид НЕ считает и НЕ владеет результатом: разбор живёт в
    /// сеансе документа (<see cref="FsaAnalysisSession"/>), а слои, цвета и
    /// строки собирает <see cref="FsaPresentationBuilder"/> — одним снимком на
    /// график и на таблицу. Своего анализа у вида нет (критерий 4).
    ///
    /// (`A145`, этап 3) Вид отвечает ТОЛЬКО за ленты, штриховку сумм-пиков,
    /// невязку, линии и короткую строку состояния (<see cref="DrawFsaStatus"/>:
    /// «считается / невозможно / ошибка»). Перечень компонентов, пределы,
    /// предупреждения, невязка числом и строка качества живут в окне отчёта
    /// <see cref="FSAReportView"/>; прежней ручной таблицы под панелью
    /// курсора, её бюджета высоты и усечения хвоста качества здесь больше нет.
    /// </summary>
    public partial class EnergySpectrumView
    {
        /// <summary>
        /// Сеанс разбора. Документ отдаёт виду СВОЙ
        /// (<see cref="DocEnergySpectrum.FsaSession"/>); вид без документа
        /// (пробы `FsaStackShot`, `FsaQualityRowProbe`) получает собственный при
        /// первом обращении.
        /// </summary>
        FsaAnalysisSession fsaSession;

        /// <summary>Положение «родители/дочерние» — настройка представления, в отпечаток не входит.</summary>
        FsaGrouping fsaGrouping = FsaGrouping.Daughters;

        // Буфер ломаной переиспользуется, как и у контура пика (причина выбора
        // ломаной — в комментарии к DrawFsaCurve).
        readonly List<Point> fsaCurveBuffer = new List<Point>();

        /// <summary>Снимок представления, построенный для текущего результата и группировки.</summary>
        FsaPresentation fsaPresentation;

        /// <summary>
        /// (`A246`) Имя слоя состава, ВЫБРАННОГО в таблице отчёта; null —
        /// выбора нет. Ставит окно отчёта (<see cref="FSAReportView"/>), читает
        /// только отрисовка лент.
        /// </summary>
        string fsaHighlight;

        // Всё, что зависит только от разложения, а не от вьюпорта, считается
        // один раз на результат: кумулятивные кривые стека (низ и верх каждой
        // ленты), спектр за вычетом фона и точки прямых подписей. Раньше эти
        // массивы аллоцировались и суммировались на КАЖДЫЙ кадр — при 8192
        // каналах и семи слоях это сотни килобайт и лишние проходы на кадр.
        double[][] fsaCumulative;

        /// <summary>
        /// Верх подслоя сумм-пиков внутри ленты каждого слоя; null у слоя —
        /// сумм-пиков у него нет. Каскадные суммы принадлежат своему нуклиду,
        /// поэтому не отдельная лента, а штриховка внутри его же ленты: в
        /// легенде и в «пироге» они остаются частью нуклида.
        /// </summary>
        double[][] fsaSumPeakLevel;

        double[] fsaZeroLevel;
        double[] fsaNetSpectrum;

        /// <summary>
        /// Сеанс разбора, на который подписан вид. Присваивает документ при
        /// создании вида; подписка переезжает вместе с ним.
        /// </summary>
        internal FsaAnalysisSession FsaSession
        {
            get
            {
                if (this.fsaSession == null)
                {
                    this.FsaSession = new FsaAnalysisSession();
                }

                return this.fsaSession;
            }

            set
            {
                if (ReferenceEquals(this.fsaSession, value))
                {
                    return;
                }

                if (this.fsaSession != null)
                {
                    this.fsaSession.Completed -= this.FsaSessionCompleted;
                }

                this.fsaSession = value;
                if (this.fsaSession != null)
                {
                    this.fsaSession.Completed += this.FsaSessionCompleted;
                }

                this.ForgetFsaPresentation();
            }
        }

        /// <summary>
        /// Группировка строк и лент: родители/дочерние. Смена НЕ трогает сеанс
        /// и не запускает разбор — перестраивается только представление
        /// (критерий 7). Ставит окно отчёта (этап 3).
        /// </summary>
        internal FsaGrouping FsaGrouping
        {
            get
            {
                return this.fsaGrouping;
            }

            set
            {
                if (this.fsaGrouping == value)
                {
                    return;
                }

                this.fsaGrouping = value;
                this.ForgetFsaPresentation();
                this.Invalidate();
            }
        }

        /// <summary>
        /// (`A246`, решение Amber 05.09.2026) ВЫДЕЛЕННЫЙ КОМПОНЕНТ СОСТАВА —
        /// имя слоя (<see cref="FsaStackLayer.Name"/>), выбранного строкой в
        /// таблице отчёта; null — выбора нет.
        ///
        /// Выделение делается ПРИГЛУШЕНИЕМ ОСТАЛЬНЫХ: выбранная лента остаётся
        /// в полном цвете, прочие бледнеют (<see cref="MuteFsaColor"/>).
        /// Штриховка на эту роль не годится — она занята сумм-пиками.
        ///
        /// ⛔ Ни расчёта, ни представления это не трогает: ни один слой, ни одна
        /// кривая и ни одно число от выделения не меняются — меняется ТОЛЬКО
        /// краска. Поэтому здесь не сбрасывается ни снимок представления, ни
        /// кадровые массивы, и пересчёт не заказывается: одна перерисовка.
        /// </summary>
        internal string FsaHighlight
        {
            get
            {
                return this.fsaHighlight;
            }

            set
            {
                string next = string.IsNullOrEmpty(value) ? null : value;
                if (string.Equals(this.fsaHighlight, next, StringComparison.Ordinal))
                {
                    return;
                }

                this.fsaHighlight = next;
                this.Invalidate();
            }
        }

        /// <summary>Доля непрозрачности приглушённой ленты (у полноцветной — 230).</summary>
        const int MutedBandAlpha = 64;

        /// <summary>Насколько приглушённый цвет сдвинут к своему же серому, доля.</summary>
        const double MutedBandDesaturation = 0.6;

        /// <summary>
        /// (`A246`) ПРИГЛУШЁННЫЙ цвет ленты: та же краска, сдвинутая к своему
        /// серому и залитая много прозрачнее.
        ///
        /// ⚠ Приглушение сделано ПРОЗРАЧНОСТЬЮ, а не подмешиванием белого или
        /// чёрного, НАРОЧНО — и это единственный способ, работающий на обеих
        /// темах: цвет поля графика задаёт человек
        /// (<see cref="ColorConfig.BackgroundColor"/>), и лента с малой альфой
        /// уходит к ТОМУ фону, какой под ней есть, — к светлому на светлой теме
        /// и к тёмному на тёмной. Подмешивание постоянного цвета на одной из
        /// двух тем давало бы обратное: «приглушённая» лента становилась бы
        /// КОНТРАСТНЕЕ полноцветной.
        ///
        /// Ленты стека не перекрываются (слой k занимает полосу между
        /// накоплениями k−1 и k), поэтому под приглушённой лентой лежит поле, а
        /// не соседний слой, и прозрачность не путает цвета между собой.
        /// </summary>
        static Color MuteFsaColor(Color color)
        {
            double grey = 0.30 * color.R + 0.59 * color.G + 0.11 * color.B;
            return Color.FromArgb(MutedBandAlpha,
                                  MuteChannel(color.R, grey),
                                  MuteChannel(color.G, grey),
                                  MuteChannel(color.B, grey));
        }

        static int MuteChannel(int value, double grey)
        {
            int muted = (int)Math.Round(value + (grey - value) * MutedBandDesaturation);
            return muted < 0 ? 0 : muted > 255 ? 255 : muted;
        }

        bool IsFsaMode()
        {
            return this.backgroundMode == BackgroundMode.ShowFSA;
        }

        /// <summary>Спектр за вычетом фона — то, что нарисовано в режиме FSA.</summary>
        internal double[] FsaNetSpectrum
        {
            get
            {
                return this.IsFsaVisible() ? this.fsaNetSpectrum : null;
            }
        }

        /// <summary>
        /// Поднять верхнюю границу вертикальной шкалы до модели разложения:
        /// шкала считается по спектрам, а модель бывает выше — тогда верх стека
        /// уходит за поле и до него не докрутить.
        /// </summary>
        void ExtendBoundariesWithFsaModel(int firstChannel, int lastChannel, ref double maximum)
        {
            if (!this.IsFsaMode())
            {
                return;
            }

            // Снимок, а не два обращения к свойству: разложение публикуется
            // фоновым потоком, и на каждом пути отказа (пустая библиотека,
            // Analyze вернул null, исключение) оно становится null. Проверить
            // одно чтение и разыменовать другое — значит однажды упасть здесь.
            FsaResult result = this.FsaSession.Result;
            double[] model = result != null ? result.Model : null;
            if (model == null)
            {
                return;
            }

            double scale = this.verticalUnit == VerticalUnit.CountsPerSecond && this.energySpectrum.MeasurementTime != 0.0
                ? 1.0 / this.energySpectrum.MeasurementTime
                : 1.0;
            int from = Math.Max(0, firstChannel);
            int to = Math.Min(model.Length - 1, lastChannel);
            for (int i = from; i <= to; i++)
            {
                double value = model[i] * scale;
                if (value > maximum)
                {
                    maximum = value;
                }
            }
        }

        bool IsFsaVisible()
        {
            return this.IsFsaMode() && this.FsaSession.Result != null;
        }

        /// <summary>
        /// Поставить разложение в очередь, если оно устарело. Вызывается при
        /// подготовке данных вида, то есть на UI-потоке; сам счёт уходит в фон.
        /// ⚠ Имя оставлено прежним: его зовёт <c>PrepareViewData</c> в
        /// <c>EnergySpectrumView.cs</c>.
        /// </summary>
        void UpdateFsaOverlay()
        {
            this.FsaSession.EnsureUpToDate(this.activeResultData, this.backgroundEnergySpectrum != null);
        }

        /// <summary>
        /// Забыть разложение: оно принадлежит прежнему спектру. Вызывается при
        /// смене активного спектра — иначе стек предыдущего дорисовывался бы
        /// поверх нового до конца пересчёта. ⚠ Имя оставлено прежним: его зовёт
        /// <c>ActiveResultDataIndex</c> в <c>EnergySpectrumView.cs</c>.
        /// </summary>
        internal void ResetFsaOverlay()
        {
            this.FsaSession.Reset();
            this.ForgetFsaPresentation();
        }

        void ForgetFsaPresentation()
        {
            this.fsaPresentation = null;
            this.fsaCumulative = null;
            this.fsaSumPeakLevel = null;
            this.fsaZeroLevel = null;
            this.fsaNetSpectrum = null;
        }

        void FsaSessionCompleted(object sender, EventArgs e)
        {
            // Событие приходит из фонового потока: перерисовку заказываем на UI.
            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        this.fsaPresentation = null;

                        // «Отложить, а не выбросить»: сеанс сам дочитывает
                        // очередь из последнего снимка (критерий 10), но
                        // снимок берётся на UI-потоке — и если входные данные
                        // сменились, пока НИКТО не звал `EnsureUpToDate`,
                        // заказать счёт заново некому: `UpdateFsaOverlay`
                        // живёт в подготовке данных вида, а на статичном
                        // спектре её больше никто не позовёт. Здесь мы уже
                        // на UI-потоке и с текущими данными — отпечаток
                        // сойдётся, и вызов молча вернётся.
                        if (this.IsFsaMode())
                        {
                            this.UpdateFsaOverlay();
                        }

                        this.Invalidate();

                        // (`A50`, `A145` этап 3) Отвергнутую матрицу называет
                        // ДОКУМЕНТ (<see cref="DocEnergySpectrum"/>), а не вид:
                        // сообщение не должно зависеть ни от режима графика,
                        // ни от видимости окна отчёта, и идти из отрисовки
                        // ему тоже нельзя. Вид без документа (пробы) о
                        // матрице не говорит — читатель у неё один.
                    });
                }
            }
            catch (Exception)
            {
                // окно успело закрыться — рисовать уже некому
            }
        }

        /// <summary>
        /// Снимок представления для этого результата. Перестраивается, когда
        /// сменился результат, группировка или признак старой матрицы; и лента
        /// на графике, и строка таблицы читают ЕГО (критерий 4).
        /// </summary>
        FsaPresentation GetFsaPresentation(FsaResult result)
        {
            bool oldFormat = this.FsaSession.ResponseMatrixOldFormat;
            if (this.fsaPresentation == null
                || !ReferenceEquals(this.fsaPresentation.Source, result)
                || this.fsaPresentation.RequestedGrouping != this.fsaGrouping
                || this.fsaPresentation.MatrixOldFormat != oldFormat)
            {
                this.fsaPresentation = FsaPresentationBuilder.Build(result, this.fsaGrouping, oldFormat);
                this.BuildFsaFrameData(result);
            }

            return this.fsaPresentation;
        }

        /// <summary>
        /// Подготовка всего, что не зависит от вьюпорта: кумулятивные кривые
        /// стека, спектр за вычетом вычтенного фона и якоря подписей. Считается
        /// один раз на результат разложения, а не на кадр.
        /// </summary>
        void BuildFsaFrameData(FsaResult result)
        {
            List<FsaStackLayer> layers = this.fsaPresentation.Layers;
            int channels = this.energySpectrum != null ? this.energySpectrum.NumberOfChannels : 0;
            int count = layers.Count;
            this.fsaZeroLevel = new double[channels];
            this.fsaCumulative = new double[count][];

            this.fsaSumPeakLevel = new double[count][];

            double[] running = this.fsaZeroLevel;
            for (int k = 0; k < count; k++)
            {
                double[] curve = layers[k].Curve;
                double[] level = new double[channels];
                for (int i = 0; i < channels; i++)
                {
                    level[i] = running[i] + (i < curve.Length ? curve[i] : 0.0);
                }

                // Подслой сумм-пиков кладётся на НИЗ ленты: так его высота
                // читается от границы со слоем ниже, а не висит в середине.
                double[] sums = layers[k].SumPeakCurve;
                if (sums != null)
                {
                    double[] sumLevel = new double[channels];
                    for (int i = 0; i < channels; i++)
                    {
                        double top = running[i] + (i < sums.Length ? sums[i] : 0.0);

                        // Выше собственной ленты подслой не поднимается.
                        //
                        // С 13.08.2026 это ЗАСЛОН, а не поправка: подслой
                        // строится той же гистограммой и тем же ядром, что и
                        // лента (`FsaAnalyzer.BuildSumPeakCurve`), и обогнать её
                        // не может по построению. До того он строился своей
                        // копией кода и обгонял — а подрезка здесь это молча
                        // прятала, отчего дефект и не увидели глазами: нашла
                        // его счётом проба (S37).
                        sumLevel[i] = top < level[i] ? top : level[i];
                    }

                    this.fsaSumPeakLevel[k] = sumLevel;
                }

                this.fsaCumulative[k] = level;
                running = level;
            }

            // Правило «чистого спектра» — у результата, одно на вид и на пробы
            // (`S88`): вторая его копия рядом разъехалась бы молча.
            this.fsaNetSpectrum = result.NetSpectrum(
                this.energySpectrum != null ? this.energySpectrum.Spectrum : null);
        }

        /// <summary>
        /// Стек компонентов и линия измеренного спектра поверх него. Рисуется
        /// вместо обычной заливки активного спектра.
        ///
        /// Возвращает false, если рисовать нечем: режим не тот, разложение ещё
        /// не готово или уже сброшено. Решение принимается ЗДЕСЬ, по одному
        /// снимку результата, а не проверкой у вызывающего: раздельные проверка
        /// и отрисовка расходились между собой (разложение успевало исчезнуть
        /// между ними), и кадр оставался вовсе без активного спектра.
        /// ⚠ Имя оставлено прежним: его зовёт <c>DrawChart</c> в
        /// <c>EnergySpectrumView.cs</c> и отражением — `FsaStackShot`.
        /// </summary>
        bool ShowFsaOverlay(Graphics g)
        {
            if (!this.IsFsaMode())
            {
                return false;
            }

            FsaResult result = this.FsaSession.Result;
            if (result == null)
            {
                return false;
            }

            List<FsaStackLayer> layers = this.GetFsaPresentation(result).Layers;
            if (layers.Count == 0 || this.fsaCumulative == null)
            {
                return false;
            }

            // Заливка идёт БЕЗ антиалиасинга: ленты стека — вертикальные
            // однопиксельные полоски, сглаживать в них нечего, а GDI+ платит за
            // AA-путь полную цену (в отрисовке пиков это была разница 130 против
            // 56 мс на кадр). Сглаживание включается ниже и только под линии.
            SmoothingMode savedSmoothing = g.SmoothingMode;
            PixelOffsetMode savedPixelOffset = g.PixelOffsetMode;
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Default;
            // (`A246`) Приглушение включается ТОЛЬКО когда выбранный компонент
            // и вправду нарисован лентой. Выбор строки без ленты (необнаруженный
            // кандидат, невязка, качество) не приглушает ничего: поблекший
            // график, на котором не подсвечено НИЧЕГО, врал бы о том, что
            // выбранного на нём нет вовсе.
            string highlight = this.fsaHighlight;
            bool muting = false;
            if (highlight != null)
            {
                for (int k = 0; k < layers.Count; k++)
                {
                    if (string.Equals(layers[k].Name, highlight, StringComparison.Ordinal))
                    {
                        muting = true;
                        break;
                    }
                }
            }

            try
            {
                for (int k = 0; k < layers.Count; k++)
                {
                    double[] lower = k > 0 ? this.fsaCumulative[k - 1] : this.fsaZeroLevel;
                    Color color = this.fsaPresentation.ColorOf(layers[k].Name);
                    bool muted = muting && !string.Equals(layers[k].Name, highlight, StringComparison.Ordinal);
                    Color painted = muted ? MuteFsaColor(color) : Color.FromArgb(230, color);
                    using (Brush brush = new SolidBrush(painted))
                    {
                        this.DrawFsaBand(g, brush, lower, this.fsaCumulative[k]);
                    }

                    // Каскадные суммы — штриховкой поверх собственной ленты, в
                    // её же цвете: это не другой компонент, а часть этого же
                    // нуклида, и своей строки в легенде у них нет.
                    double[] sumLevel = this.fsaSumPeakLevel != null ? this.fsaSumPeakLevel[k] : null;
                    if (sumLevel != null)
                    {
                        // Подслой сумм-пиков блекнет ВМЕСТЕ со своей лентой: он
                        // часть того же нуклида, и оставленный ярким — выдал бы
                        // приглушённый компонент за выбранный.
                        Color hatchColor = muted
                            ? MuteFsaColor(FsaPalette.SumPeakHatchColor(color))
                            : FsaPalette.SumPeakHatchColor(color);
                        using (Brush hatch = new HatchBrush(HatchStyle.DarkUpwardDiagonal,
                                                            hatchColor, painted))
                        {
                            this.DrawFsaBand(g, hatch, lower, sumLevel);
                        }
                    }
                }
            }
            finally
            {
                g.SmoothingMode = savedSmoothing;
                g.PixelOffsetMode = savedPixelOffset;
            }

            // ⛔ НЕВЯЗКА — ОТДЕЛЬНАЯ СУЩНОСТЬ НА ЭКРАНЕ (решение Amber
            // 01.09.2026, `S111`). Всё, что не описано моделью по имеющимся
            // нуклидам, обязано быть ВИДНО, а не выводиться читателем из зазора
            // между двумя кривыми.
            //
            // Рисуется лентой между верхом стека (модель) и измерением, и
            // ОБЕИМИ ЗНАКАМИ — по прямому указанию Amber:
            //   • измерение ВЫШЕ модели — «не описано», модели не хватило;
            //   • модель ВЫШЕ измерения — «приписано лишнее», и это не мелочь:
            //     измерено вторым проходом (`S111`) — такие места есть у ВСЕХ
            //     117 спектров корпуса, и до сих пор на них не смотрел никто.
            //
            // ⚠ Штриховка, а не заливка, и НАРОЧНО: невязка не компонент, у неё
            // нет ни нуклида, ни амплитуды фита, и выглядеть как ещё одна лента
            // состава она не должна. Знаки разводит ЦВЕТ штриха (`A28`).
            this.DrawFsaResidual(g, this.fsaCumulative[layers.Count - 1], this.fsaNetSpectrum,
                                 this.globalConfigManager.GlobalConfig.ColorConfig
                                     .ActiveSpectrumColor.Color);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            try
            {
                // Верх стека — сумма модели: белая линия отделяет его от спектра.
                using (Pen modelPen = new Pen(Color.FromArgb(200, Color.White)))
                {
                    this.DrawFsaCurve(g, modelPen, this.fsaCumulative[layers.Count - 1]);
                }

                ColorConfig colorConfig = this.globalConfigManager.GlobalConfig.ColorConfig;
                using (Pen spectrumPen = new Pen(colorConfig.ActiveSpectrumColor.Color))
                {
                    // (`A26`) Линия измерения не рвётся: где фон перекрыл спектр,
                    // она идёт по низу поля.
                    this.DrawFsaCurve(g, spectrumPen, this.fsaNetSpectrum, clampToFloor: true);
                }
            }
            finally
            {
                g.SmoothingMode = savedSmoothing;
                g.PixelOffsetMode = savedPixelOffset;
            }

            return true;
        }

        /// <summary>
        /// (`S111`) Лента НЕВЯЗКИ между моделью и измерением, обеими знаками.
        ///
        /// ⛔ ШТРИХ У ОБЕИХ ПОЛОВИН ОДИН — ПРЯМАЯ КЛЕТКА (`HatchStyle.Cross`),
        /// а знак разводит ЦВЕТ (решение Amber 01.09.2026, `A28`):
        ///   • недобор, «модели не хватило» — клетка ЦВЕТОМ ЛИНИИ СПЕКТРА;
        ///   • перебор, «приписано лишнее» — ЧЁРНАЯ клетка
        ///     (<see cref="FsaPresentationBuilder.ResidualColor"/>).
        ///
        /// Так задано после замера глазами: до `A28` половины различались
        /// наклоном пары (косая против прямой) и обе были тёмно-серыми, и
        /// недобор пропадал — он лежит НАД стеком, на белом поле, где серая
        /// косая клетка не читается, тогда как перебор лежит на ярких лентах
        /// состава и виден хорошо. Цвет линии спектра выбран не для красоты: у
        /// недобора смысл «до измеренной линии не достали», и штрих того же
        /// цвета, что линия, показывает, ДО ЧЕГО не достали.
        ///
        /// ⚠ Полосы строятся ЯВНО через min/max, а не подачей кривых «как
        /// есть»: <see cref="DrawFsaBand"/> заливает многоугольник между двумя
        /// линиями и на перевёрнутой паре нарисовал бы ленту там, где её нет.
        /// </summary>
        /// <param name="spectrumColor">Цвет линии спектра из настроек человека —
        /// им штрихуется половина недобора.</param>
        void DrawFsaResidual(Graphics g, double[] model, double[] measured, Color spectrumColor)
        {
            if (model == null || measured == null || model.Length == 0
                || measured.Length != model.Length)
            {
                return;
            }

            var above = new double[model.Length];
            var below = new double[model.Length];
            for (int i = 0; i < model.Length; i++)
            {
                above[i] = Math.Max(model[i], measured[i]);
                below[i] = Math.Min(model[i], measured[i]);
            }

            // «Не добавлено» (минус в строке): клетка цветом линии спектра.
            using (Brush missing = new HatchBrush(HatchStyle.Cross,
                                                  Color.FromArgb(200, spectrumColor),
                                                  Color.Transparent))
            {
                this.DrawFsaBand(g, missing, model, above);
            }

            // «Добавлено больше чем надо» (плюс в строке): та же клетка, чёрная.
            using (Brush excess = new HatchBrush(HatchStyle.Cross,
                                                 Color.FromArgb(200, FsaPresentationBuilder.ResidualColor),
                                                 Color.Transparent))
            {
                this.DrawFsaBand(g, excess, below, model);
            }
        }

        /// <summary>Лента между двумя кривыми — тем же способом, что заливка пиков.</summary>
        void DrawFsaBand(Graphics g, Brush brush, double[] lower, double[] upper)
        {
            int rightEdge = this.CalcMaximumXValue() + this.scrollX + this.left;
            int firstPixel = Math.Max(this.VisibleLeftPixel, this.left);
            int maxPixel = Math.Min(this.VisibleRightPixel, rightEdge - 1);
            if (firstPixel > maxPixel)
            {
                return;
            }

            int[] pixelChannels = this.EnsurePixelChannelMap(this.energySpectrum, this.energyCalibration, firstPixel, maxPixel);
            this.spectrumFillPath.Reset();
            this.peakBandTop.Clear();
            this.peakBandBottom.Clear();
            int previousX = Int32.MinValue;
            for (int x = firstPixel; x <= maxPixel; x++)
            {
                int channel = pixelChannels[x - firstPixel];
                if (channel < 0 || channel >= lower.Length)
                {
                    continue;
                }

                double lowerValue = this.ScaleFsaValue(lower[channel]);
                double upperValue = this.ScaleFsaValue(upper[channel]);
                if (upperValue <= 0.0)
                {
                    continue;
                }

                int top = this.GetSpectrumValueY(upperValue);
                int bottom = lowerValue > 0.0 ? this.GetSpectrumValueY(lowerValue) : this.height;
                if (bottom > this.height)
                {
                    bottom = this.height;
                }

                if (x <= this.left || bottom <= top)
                {
                    continue;
                }

                if (this.peakBandTop.Count > 0 && x != previousX + 1)
                {
                    this.FlushPeakBand();
                }

                this.peakBandTop.Add(new Point(x, top));
                this.peakBandTop.Add(new Point(x + 1, top));
                this.peakBandBottom.Add(new Point(x, bottom));
                this.peakBandBottom.Add(new Point(x + 1, bottom));
                previousX = x;
            }

            this.FlushPeakBand();
            if (this.spectrumFillPath.PointCount > 0)
            {
                g.FillPath(brush, this.spectrumFillPath);
            }
        }

        /// <summary>
        /// Кривая по каналам линией — для суммы модели и спектра. Ломаная
        /// копится в переиспользуемый буфер и отдаётся одним DrawLines; разрыв
        /// (нулевое значение, вылет за поле) закрывает текущую ломаную.
        ///
        /// Причина здесь не в скорости: замер (1500 px, две кривые, AA) даёт
        /// 3.7 мс/кадр посегментно против 5.9 одной ломаной — для тонкого пера
        /// без капов batching чуть дороже, выигрыш в отрисовке пиков был у пера
        /// 2.1f с round-капами (10.2 против 6.7 в том же замере). Дело в краске:
        /// линия верха стека полупрозрачная, и посегментный DrawLine кладёт её в
        /// стыках дважды — 8133 пикселя расходятся с ломаной, максимум дельты
        /// 53/255, то есть по всей кривой видны тёмные точки на изломах.
        /// </summary>
        /// <param name="clampToFloor">
        /// (`A26`, решение Amber 01.09.2026) Значение, которому на шкале места
        /// нет — ноль или минус, — ПРИЖИМАТЬ К НИЗУ ПОЛЯ, а не рвать ломаную.
        ///
        /// Ставится линии ИЗМЕРЕНИЯ и только ей. В режиме FSA рисуется спектр за
        /// вычетом фона, и там, где фон перекрыл измерение, разность уходит в
        /// ноль или минус: у `Чароит в домике` с его домашним фоном это 1026
        /// каналов из 8192, все в полосе 1800…3400 кэВ, — и линия рассыпалась
        /// там на куски. Ноль и минус на графике выглядят одинаково («упёрлась в
        /// пол»), и это сознательно: на степенной и логарифмической шкале точки
        /// «ниже нуля» не существует вовсе.
        ///
        /// ⛔ Линии МОДЕЛИ прижатие НЕ ставится: её ноль означает «образа тут
        /// нет», и стек в этом месте тоже ничего не рисует, — линия по нижнему
        /// краю говорила бы о модели там, где её нет.
        /// </param>
        void DrawFsaCurve(Graphics g, Pen pen, double[] values, bool clampToFloor = false)
        {
            if (values == null)
            {
                return;
            }

            int rightEdge = this.CalcMaximumXValue() + this.scrollX + this.left;
            int firstPixel = Math.Max(this.VisibleLeftPixel, this.left);
            int maxPixel = Math.Min(this.VisibleRightPixel, rightEdge - 1);
            if (firstPixel > maxPixel)
            {
                return;
            }

            int[] pixelChannels = this.EnsurePixelChannelMap(this.energySpectrum, this.energyCalibration, firstPixel, maxPixel);
            this.fsaCurveBuffer.Clear();
            int previousX = Int32.MinValue;
            for (int x = firstPixel; x <= maxPixel; x++)
            {
                int channel = pixelChannels[x - firstPixel];

                // Разрыв остаётся один и настоящий: под этим пикселем НЕТ
                // канала. Всё прочее — вопрос места на шкале, а не отсутствия
                // данных, и рвать по нему ломаную нельзя (`A26`).
                bool broken = channel < 0 || channel >= values.Length;
                int y = 0;
                if (!broken)
                {
                    double value = this.ScaleFsaValue(values[channel]);
                    if (value <= 0.0)
                    {
                        if (clampToFloor)
                        {
                            y = this.height;
                        }
                        else
                        {
                            broken = true;
                        }
                    }
                    else
                    {
                        y = this.GetSpectrumValueY(value);
                        if (y > this.height)
                        {
                            if (clampToFloor)
                            {
                                y = this.height;
                            }
                            else
                            {
                                broken = true;
                            }
                        }
                        else if (y < 0)
                        {
                            y = 0;
                        }
                    }
                }

                if (broken)
                {
                    this.FlushFsaCurve(g, pen);
                    previousX = Int32.MinValue;
                    continue;
                }

                if (previousX != Int32.MinValue && x != previousX + 1)
                {
                    this.FlushFsaCurve(g, pen);
                }

                this.fsaCurveBuffer.Add(new Point(x, y));
                previousX = x;
            }

            this.FlushFsaCurve(g, pen);
        }

        void FlushFsaCurve(Graphics g, Pen pen)
        {
            if (this.fsaCurveBuffer.Count >= 2)
            {
                g.DrawLines(pen, this.fsaCurveBuffer.ToArray());
            }
            else if (this.fsaCurveBuffer.Count == 1)
            {
                // Одиночная точка: DrawLines её не принимает, а участок кривой
                // шириной в пиксель на графике всё равно должен остаться.
                Point only = this.fsaCurveBuffer[0];
                g.DrawLine(pen, only, only);
            }

            this.fsaCurveBuffer.Clear();
        }

        double ScaleFsaValue(double value)
        {
            if (this.verticalUnit == VerticalUnit.CountsPerSecond && this.energySpectrum.MeasurementTime != 0.0)
            {
                return value / this.energySpectrum.MeasurementTime;
            }

            return value;
        }

        // Где встали панели значений курсора в этом кадре: их может быть одна
        // (канал) или две (канал + выделенная область), и они переезжают слева
        // направо вслед за курсором. Строка состояния разложения ставится под
        // левой из них — одна, общая. ⚠ Оба метода ниже зовёт чужой
        // `EnergySpectrumView.cs` (панели курсора), имена оставлены.
        int cursorPanelLeft;
        int cursorPanelBottom;
        bool cursorPanelRegistered;

        void ResetCursorPanelBounds(int anchorX, int anchorY)
        {
            this.cursorPanelLeft = anchorX;
            this.cursorPanelBottom = anchorY;
            this.cursorPanelRegistered = false;
        }

        void RegisterCursorPanel(int x, int y, int height)
        {
            // Низ берётся у ЛЕВОЙ панели, а не самый нижний из всех: строка
            // встаёт под левой, и общий максимум отрывал её от неё на всю
            // разницу высот панелей. Какая из панелей левая — зависит от того,
            // с какой стороны курсор, поэтому условие по x, а не по виду панели.
            if (!this.cursorPanelRegistered || x < this.cursorPanelLeft)
            {
                this.cursorPanelLeft = x;
                this.cursorPanelBottom = y + height;
                this.cursorPanelRegistered = true;
                return;
            }

            if (x == this.cursorPanelLeft)
            {
                int bottom = y + height;
                if (bottom > this.cursorPanelBottom)
                {
                    this.cursorPanelBottom = bottom;
                }
            }
        }

        /// <summary>
        /// Короткая строка состояния разложения под левой панелью значений
        /// курсора. ⚠ Имя оставлено прежним: его зовёт <c>DrawChart</c> в
        /// чужом <c>EnergySpectrumView.cs</c>. Таблицы состава здесь больше
        /// нет (`A145`, этап 3) — она в окне отчёта <see cref="FSAReportView"/>.
        /// </summary>
        void ShowFsaTable(Graphics g, int width)
        {
            if (this.IsFsaMode())
            {
                this.DrawFsaStatus(g, this.cursorPanelLeft, this.cursorPanelBottom + 6, width);
            }
        }

        /// <summary>
        /// Что вид говорит на графике о разложении, когда сказать есть что:
        /// пока результата нет — «считается», причина невозможности или
        /// ошибка (<see cref="FsaAnalysisSession.Status"/>); при готовом
        /// результате и идущем пересчёте — «считается» (`A32`: пересчёт виден
        /// ВСЕГДА, иначе на экране висит устаревший стек без единого признака).
        /// Пусто — рисовать нечего: результат есть и он актуален. Полный отчёт
        /// и строка качества здесь НЕ дублируются (`A145`, «Связь окна с
        /// главным интерфейсом»).
        /// </summary>
        public static string FsaStatusText(FsaAnalysisSession session)
        {
            if (session == null)
            {
                return null;
            }

            // Один снимок на кадр: фон публикует результат в любой момент.
            FsaResult result = session.Result;
            string status = session.Status;
            if (result == null)
            {
                return string.IsNullOrEmpty(status) ? null : status;
            }

            return session.IsRunning ? Resources.FSACalculating : null;
        }

        /// <summary>
        /// Строка состояния тем же видом, что панели значений курсора: тень,
        /// белая заливка, чёрная рамка; текст переносится по ширине панели.
        /// Пересчёт при живом результате — оранжевым: это не часть состава, а
        /// предупреждение, что состав СЕЙЧАС переписывается.
        /// </summary>
        void DrawFsaStatus(Graphics g, int x, int y, int width)
        {
            FsaAnalysisSession session = this.FsaSession;
            string text = FsaStatusText(session);
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            bool recalculating = session.Result != null;
            int height = (int)Math.Ceiling(g.MeasureString(text, this.Font, width - 12).Height) + 8;
            g.FillRectangle(Brushes.DarkGray, x + 3, y + 3, width, height);
            g.FillRectangle(Brushes.White, x, y, width, height);
            g.DrawRectangle(Pens.Black, x, y, width, height);
            Rectangle r = new Rectangle(x + 8, y + 4, width - 12, height - 8);
            g.DrawString(text, this.Font, recalculating ? Brushes.DarkOrange : Brushes.Black, r);
        }
    }
}
