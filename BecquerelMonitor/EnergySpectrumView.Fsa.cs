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
    /// </summary>
    public partial class EnergySpectrumView
    {
        /// <summary>
        /// Сколько нуклидов называется в легенде поимённо; остальные идут одной
        /// строкой «other». Мешающие образы (рентген, пики вылета) сюда НЕ
        /// считаются и показываются сверх лимита.
        ///
        /// Шесть → девять, решение Amber 18.08.2026 (`S71`): на
        /// `Th232_29.07.2022.xml` «other» набрал 8.30 % — больше трёх
        /// показанных строк вместе взятых.
        /// </summary>
        const int FsaMaxNamedLayers = 9;

        readonly FsaOverlay fsaOverlay = new FsaOverlay();

        // Буфер ломаной переиспользуется, как и у контура пика (причина выбора
        // ломаной — в комментарии к DrawFsaCurve).
        readonly List<Point> fsaCurveBuffer = new List<Point>();

        bool fsaCompletedSubscribed;
        List<FsaStackLayer> fsaLayers;
        Dictionary<string, Color> fsaColors;
        FsaResult fsaLayersSource;

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
            FsaResult result = this.fsaOverlay.Result;
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
            return this.IsFsaMode() && this.fsaOverlay.Result != null;
        }

        /// <summary>
        /// Поставить разложение в очередь, если оно устарело. Вызывается при
        /// подготовке данных вида, то есть на UI-потоке; сам счёт уходит в фон.
        /// </summary>
        void UpdateFsaOverlay()
        {
            if (!this.fsaCompletedSubscribed)
            {
                this.fsaOverlay.Completed += this.FsaOverlayCompleted;
                this.fsaCompletedSubscribed = true;
            }

            this.fsaOverlay.EnsureUpToDate(this.activeResultData, this.backgroundEnergySpectrum != null);
        }

        /// <summary>
        /// Забыть разложение: оно принадлежит прежнему спектру. Вызывается при
        /// смене активного спектра — иначе стек предыдущего дорисовывался бы
        /// поверх нового до конца пересчёта.
        /// </summary>
        internal void ResetFsaOverlay()
        {
            this.fsaOverlay.Reset();
            this.fsaLayers = null;
            this.fsaColors = null;
            this.fsaLayersSource = null;
            this.fsaCumulative = null;
            this.fsaSumPeakLevel = null;
            this.fsaZeroLevel = null;
            this.fsaNetSpectrum = null;
        }

        void FsaOverlayCompleted(object sender, EventArgs e)
        {
            // Событие приходит из фонового потока: перерисовку заказываем на UI.
            try
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        this.fsaLayers = null;

                        // «Отложить, а не выбросить»: пока счёт шёл, входные
                        // данные могли смениться — сдвинули SNR, переключили
                        // галку состава (S57), — а `EnsureUpToDate` при занятом
                        // счётчике заказ ОТБРАСЫВАЕТ и сам к нему не
                        // возвращается. Ставить заказ заново некому: по
                        // окончании счёта вид только перерисовывается, а
                        // `UpdateFsaOverlay` живёт в подготовке данных вида, и
                        // на статичном спектре её больше никто не позовёт.
                        // Здесь мы уже на UI-потоке и с текущими данными —
                        // отпечаток сойдётся, и вызов молча вернётся.
                        if (this.IsFsaMode())
                        {
                            this.UpdateFsaOverlay();
                        }

                        this.Invalidate();
                    });
                }
            }
            catch (Exception)
            {
                // окно успело закрыться — рисовать уже некому
            }
        }

        List<FsaStackLayer> GetFsaLayers(FsaResult result)
        {
            if (this.fsaLayers == null || !ReferenceEquals(this.fsaLayersSource, result))
            {
                this.fsaLayers = result.BuildStackedLayers(FsaMaxNamedLayers);
                this.fsaLayersSource = result;
                // Цвет зависит от состава кадра: место в палитре берётся по
                // имени, а занятое отдаётся следующему свободному. Значит
                // раздавать цвета надо один раз на весь список, иначе отрисовка
                // и легенда разрешили бы столкновения по-разному.
                this.fsaColors = FsaPalette.Assign(this.fsaLayers.ConvertAll(l => l.Name));
                this.BuildFsaFrameData(result);
            }

            return this.fsaLayers;
        }

        /// <summary>Цвет слоя из раздачи, посчитанной для этого разложения.</summary>
        Color FsaColorOf(string name)
        {
            Color color;
            return this.fsaColors != null && name != null && this.fsaColors.TryGetValue(name, out color)
                ? color
                : Color.Gray;
        }

        /// <summary>
        /// Подготовка всего, что не зависит от вьюпорта: кумулятивные кривые
        /// стека, спектр за вычетом вычтенного фона и якоря подписей. Считается
        /// один раз на результат разложения, а не на кадр.
        /// </summary>
        void BuildFsaFrameData(FsaResult result)
        {
            int channels = this.energySpectrum != null ? this.energySpectrum.NumberOfChannels : 0;
            int count = this.fsaLayers.Count;
            this.fsaZeroLevel = new double[channels];
            this.fsaCumulative = new double[count][];

            this.fsaSumPeakLevel = new double[count][];

            double[] running = this.fsaZeroLevel;
            for (int k = 0; k < count; k++)
            {
                double[] curve = this.fsaLayers[k].Curve;
                double[] level = new double[channels];
                for (int i = 0; i < channels; i++)
                {
                    level[i] = running[i] + (i < curve.Length ? curve[i] : 0.0);
                }

                // Подслой сумм-пиков кладётся на НИЗ ленты: так его высота
                // читается от границы со слоем ниже, а не висит в середине.
                double[] sums = this.fsaLayers[k].SumPeakCurve;
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

            double[] net = new double[channels];
            int[] raw = this.energySpectrum != null ? this.energySpectrum.Spectrum : null;
            for (int i = 0; raw != null && i < channels && i < raw.Length; i++)
            {
                double value = raw[i];
                if (result.Background != null && i < result.Background.Length)
                {
                    value -= result.Background[i];
                }

                net[i] = value > 0.0 ? value : 0.0;
            }

            this.fsaNetSpectrum = net;
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
        /// </summary>
        bool ShowFsaOverlay(Graphics g)
        {
            if (!this.IsFsaMode())
            {
                return false;
            }

            FsaResult result = this.fsaOverlay.Result;
            if (result == null)
            {
                return false;
            }

            List<FsaStackLayer> layers = this.GetFsaLayers(result);
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
            try
            {
                for (int k = 0; k < layers.Count; k++)
                {
                    double[] lower = k > 0 ? this.fsaCumulative[k - 1] : this.fsaZeroLevel;
                    Color color = this.FsaColorOf(layers[k].Name);
                    using (Brush brush = new SolidBrush(Color.FromArgb(230, color)))
                    {
                        this.DrawFsaBand(g, brush, lower, this.fsaCumulative[k]);
                    }

                    // Каскадные суммы — штриховкой поверх собственной ленты, в
                    // её же цвете: это не другой компонент, а часть этого же
                    // нуклида, и своей строки в легенде у них нет.
                    double[] sumLevel = this.fsaSumPeakLevel != null ? this.fsaSumPeakLevel[k] : null;
                    if (sumLevel != null)
                    {
                        using (Brush hatch = new HatchBrush(HatchStyle.DarkUpwardDiagonal,
                                                            FsaSumPeakHatchColor(color),
                                                            Color.FromArgb(230, color)))
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
                    this.DrawFsaCurve(g, spectrumPen, this.fsaNetSpectrum);
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
        /// Цвет штриха для подслоя сумм-пиков: тот же цвет, но заметно темнее
        /// или светлее — смотря что видно на этом. На тёмной ленте штрих
        /// осветляется, на светлой затемняется, иначе на половине палитры
        /// штриховка сливается с заливкой и подслоя не видно вовсе.
        /// </summary>
        static Color FsaSumPeakHatchColor(Color color)
        {
            int brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
            double factor = brightness < 128 ? 1.55 : 0.55;
            return Color.FromArgb(
                230,
                Math.Min(255, (int)(color.R * factor)),
                Math.Min(255, (int)(color.G * factor)),
                Math.Min(255, (int)(color.B * factor)));
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
        void DrawFsaCurve(Graphics g, Pen pen, double[] values)
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
                bool broken = channel < 0 || channel >= values.Length;
                int y = 0;
                if (!broken)
                {
                    double value = this.ScaleFsaValue(values[channel]);
                    if (value <= 0.0)
                    {
                        broken = true;
                    }
                    else
                    {
                        y = this.GetSpectrumValueY(value);
                        if (y > this.height)
                        {
                            broken = true;
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
        // направо вслед за курсором. Список состава ставится под левой из них —
        // один, общий.
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
            // Низ берётся у ЛЕВОЙ панели, а не самый нижний из всех. Список
            // состава встаёт под левой, и общий максимум отрывал его от неё на
            // всю разницу высот: панель выделенной области вдвое-втрое выше
            // панели канала, и при выделении области список уезжал вниз
            // отдельным куском. Какая из панелей левая — зависит от того, с
            // какой стороны курсор, поэтому условие по x, а не по виду панели.
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
        /// Состав разложения — одной таблицей под левой панелью значений
        /// курсора. Внутрь них список не вписывается: панель
        /// выделенной области появляется и исчезает, и список прыгал бы между
        /// таблицами.
        /// </summary>
        void ShowFsaTable(Graphics g, int width)
        {
            if (this.IsFsaMode())
            {
                this.DrawFsaOwnTable(g, this.cursorPanelLeft, this.cursorPanelBottom + 6, width);
            }
        }

        // ------------------------------------------------------------------
        // Легенда: строки «пирога» вписываются в ту же таблицу значений
        // курсора, что и канал с энергией, — снизу, тем же стилем.
        // ------------------------------------------------------------------

        const int FsaTableRowHeight = 16;

        /// <summary>Сколько строк займёт разложение в таблице курсора.</summary>
        int FsaTableRowCount(FsaResult result, string status)
        {
            if (result == null)
            {
                return string.IsNullOrEmpty(status) ? 0 : 1;
            }

            // +2 — строка качества и строка невязки модели (S51), плюс по
            // строке на каждый слой со своими сумм-пиками, плюс по строке
            // «< МДА» на каждого НЕвошедшего кандидата (S9, решение Amber
            // 14.08.2026 «показывай») и ещё одна, если кто-то из них свёрнут
            // порогом выхода (S69), плюс строка «БЕЗ ФОНА», когда фон не
            // вычитался (S44).
            return this.GetFsaLayers(result).Count + 2 + this.FsaSumPeakLayers(result).Count
                   + FsaUndetectedNamed(result).Count
                   + (FsaUndetectedFolded(result).Count > 0 ? 1 : 0)
                   + (result.SuppressedImages != null
                      && result.SuppressedImages.Count > 0 ? 1 : 0)
                   + (result.BackgroundUsed ? 0 : 1);
        }

        /// <summary>
        /// НЕ вошедшие в состав кандидаты библиотеки с определённым пределом
        /// обнаружения (S9). Строка «Cs-137 &lt; 0.3 %» — метрологический
        /// ответ «не обнаружен»: без предела он полответа, потому что «не нашли»
        /// и «не могли найти» — разные вещи. Вырожденные и без предела не
        /// показываются: врать порогом, которого нет, хуже, чем молчать.
        /// </summary>
        static List<FsaCharacteristicLimit> FsaUndetected(FsaResult result)
        {
            List<FsaCharacteristicLimit> found = new List<FsaCharacteristicLimit>();
            if (result.CharacteristicLimits == null)
            {
                return found;
            }

            foreach (FsaCharacteristicLimit limit in result.CharacteristicLimits)
            {
                if (!limit.Detected && !limit.Degenerate
                    && !double.IsNaN(limit.DetectionLimitRate)
                    && limit.DetectionLimitRate > 0.0)
                {
                    found.Add(limit);
                }
            }

            return found;
        }

        /// <summary>
        /// Наименьший суммарный выход излучений нуклида (γ и X на собственный
        /// распад, %), при котором кандидат ещё показывается СВОЕЙ строкой.
        ///
        /// ⛔ Величина НАЗНАЧЕНА Amber 18.08.2026 (`S69`), а не выведена
        /// разверткой по корпусу, — в отличие от порога `S57`. Кто станет
        /// «уточнять» её замером, пусть знает это заранее.
        ///
        /// Что она делает, посчитано по `nucdb` для обоих природных рядов:
        /// выбывают Rn-220 (0.114), Po-216 (0.0019), Po-212 (0), Rn-222 (0.079),
        /// Po-218 (0), Po-214 (0.010), Bi-210 (0), Po-210 (0.001) — ровно
        /// α-излучатели и эманации; остаются Th-232 (7.42), Ra-228 (3.83),
        /// Th-228 (10.11), Ra-224 (5.01), Ra-226 (5.28), Pb-210 (26.92),
        /// K-40 (11.75).
        ///
        /// ⚠ Th-232 держится в списке K-рентгеном: по одним гаммам у него
        /// 0.284 % и он выбыл бы. Потому и считается сумма по γ И ПО X — правило
        /// априорное, свойство нуклида, а не прибора. То, что этот рентген ниже
        /// порога большинства сцинтилляторов, — вопрос ДРУГОЙ и здесь не
        /// решается.
        ///
        /// ⛔ Порог — ТОЛЬКО НА ПОКАЗ. Колонка кандидата из фита не убирается:
        /// эманацией радона видно неравновесие, когда связка `S70` выключена, —
        /// и ровно ради этого случая свободные амплитуды и оставлены.
        /// </summary>
        public const double FsaMinTotalYieldPercent = 1.0;

        /// <summary>
        /// Кандидат показывается СВОЕЙ строкой: выход у него либо приличный,
        /// либо НЕИЗВЕСТЕН.
        ///
        /// ⚠ Неизвестный выход — это не «мал», и молча прятать по нему нельзя.
        /// Априорную сумму заполняет только сборка из баз
        /// (<see cref="FsaSampleLibrary"/>); на прежнем пути состава
        /// (<see cref="FsaLibrary.BuildFromPeaks"/>, галка «состав из баз»
        /// выключена — умолчание) её нет, и там список остаётся ровно таким,
        /// каким был. Практической разницы это не делает: туда кандидат
        /// попадает, только если поиск пиков уже подписал им пик, то есть
        /// линии у него заведомо видны.
        /// </summary>
        static bool FsaNamedUndetected(FsaCharacteristicLimit limit)
        {
            return double.IsNaN(limit.TotalYieldPercent)
                   || limit.TotalYieldPercent >= FsaMinTotalYieldPercent;
        }

        static List<FsaCharacteristicLimit> FsaUndetectedNamed(FsaResult result)
        {
            List<FsaCharacteristicLimit> found = new List<FsaCharacteristicLimit>();
            foreach (FsaCharacteristicLimit limit in FsaUndetected(result))
            {
                if (FsaNamedUndetected(limit))
                {
                    found.Add(limit);
                }
            }

            return found;
        }

        /// <summary>
        /// Кандидаты, свёрнутые порогом выхода в одну строку (`S69`).
        /// </summary>
        static List<FsaCharacteristicLimit> FsaUndetectedFolded(FsaResult result)
        {
            List<FsaCharacteristicLimit> found = new List<FsaCharacteristicLimit>();
            foreach (FsaCharacteristicLimit limit in FsaUndetected(result))
            {
                if (!FsaNamedUndetected(limit))
                {
                    found.Add(limit);
                }
            }

            return found;
        }

        /// <summary>
        /// Доля, которую кандидат занял бы в составе, стой его амплитуда на
        /// пределе обнаружения, % (`S68`).
        ///
        /// ⛔ Знаменатель — <see cref="FsaResult.StackTotal"/>, ТОТ ЖЕ, которым
        /// считаются доли строк состава (решение Amber 18.08.2026): колонка
        /// читается сверху вниз одной мерой, «Ra-224 8.22 % — Rn-222 &lt; 0.3 %».
        /// Прежде здесь печатался предел в имп/с, а это не зарегистрированные
        /// импульсы вовсе: вес линии в образе равен I/100 × ε(E) при профилях
        /// единичной площади, то есть амплитуда выражена в РАСПАДАХ, и
        /// «amplitude/liveTime» есть активность в шкале поданной кривой
        /// эффективности. На `Th232_29.07.2022.xml` подпись прямо приглашала
        /// сложить несложимое: полная скорость счёта спектра 416.37 имп/с, а у
        /// Th-232 напечатано «&lt; 607 cps».
        ///
        /// NaN — считать нечем (нет предела либо стек пуст).
        /// </summary>
        static double FsaLimitSharePercent(FsaResult result, double peakCounts)
        {
            return result.StackTotal > 0.0 && !double.IsNaN(peakCounts)
                ? 100.0 * peakCounts / result.StackTotal
                : double.NaN;
        }

        /// <summary>
        /// Длина списка подавленных имён в знаках. Ограничение по ЗНАКАМ, а
        /// не по числу имён: «Backscatter180» вчетверо длиннее «W», и три
        /// имени то помещаются в строку таблицы, то вылезают за подложку.
        /// Пойман снимком дважды — сперва хвостом у строки качества, потом
        /// своей строкой при трёх длинных именах.
        /// </summary>
        const int FsaMaxSuppressedChars = 24;

        /// <summary>
        /// Имена подавленных образов (`S78`) для строки «подавлено: …».
        /// Строки не бывает вовсе, когда подавленных нет: пометка о пустоте — шум.
        ///
        /// Имена ОБРЕЗАЮТСЯ по <see cref="FsaMaxSuppressedNames"/>, а хвост
        /// показывается числом: строка качества и без того несёт χ²/ndf и до
        /// пяти пометок, а состав из десятка образов вытолкнул бы строку за
        /// ширину таблицы. Число вместо имён — не сокрытие: читателю сказано,
        /// СКОЛЬКО их, и по ключу `--lib-dump` пробы список выписывается целиком.
        /// </summary>
        static List<string> FsaSuppressedNames(FsaResult result)
        {
            List<string> names = new List<string>();
            int used = 0;
            for (int k = 0; k < result.SuppressedImages.Count; k++)
            {
                string name = FsaPalette.DisplayName(result.SuppressedImages[k].Name);
                if (names.Count > 0 && used + name.Length > FsaMaxSuppressedChars)
                {
                    names.Add(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                            Resources.FSASuppressedMore,
                                            result.SuppressedImages.Count - k));
                    break;
                }
                names.Add(name);
                used += name.Length + 2;
            }

            return names;
        }

        /// <summary>Формат предела в легенде: три значащие цифры, как и прежде.</summary>
        static string FsaLimitText(double sharePercent)
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                 Resources.FSAMdaValue,
                                 double.IsNaN(sharePercent)
                                     ? "?"
                                     : sharePercent.ToString("G3",
                                           System.Globalization.CultureInfo.CurrentCulture));
        }

        /// <summary>
        /// Слои, у которых есть свои сумм-пики. Каждый получает в легенде СВОЮ
        /// строку с именем нуклида: общее «заштриховано — сумм-пик» не отвечало
        /// на вопрос, чьи это суммы, а при двух нуклидах с каскадами ответить
        /// на него по одной картинке нечем.
        /// </summary>
        List<FsaStackLayer> FsaSumPeakLayers(FsaResult result)
        {
            List<FsaStackLayer> found = new List<FsaStackLayer>();
            foreach (FsaStackLayer layer in this.GetFsaLayers(result))
            {
                if (layer.SumPeakCurve != null)
                {
                    found.Add(layer);
                }
            }

            return found;
        }

        /// <summary>
        /// Строки состава: квадратик цвета слоя, имя слева, доля справа —
        /// ровно как остальные строки таблицы значений. Последняя строка —
        /// качество описания, с пометками об отсутствии кривой эффективности и
        /// об упёршемся в границу сетки дрейфе.
        /// </summary>
        void DrawFsaRows(Graphics g, Rectangle r, FsaResult result, string status)
        {
            if (result == null)
            {
                g.DrawString(status, this.Font, Brushes.Black, r);
                return;
            }

            List<FsaStackLayer> layers = this.GetFsaLayers(result);
            Rectangle nameRect = new Rectangle(r.X + 14, r.Y, r.Width - 14, r.Height);
            for (int k = 0; k < layers.Count; k++)
            {
                using (Brush swatch = new SolidBrush(this.FsaColorOf(layers[k].Name)))
                {
                    g.FillRectangle(swatch, r.Left, r.Top + 4, 10, 8);
                }

                g.DrawString(FsaPalette.DisplayName(layers[k].Name), this.Font, Brushes.Black, nameRect);
                g.DrawString(layers[k].SharePercent.ToString("n2") + Resources.PercentCharacter,
                             this.Font, Brushes.Black, r, this.farFormat);
                r.Y += FsaTableRowHeight;
                nameRect.Y += FsaTableRowHeight;
            }

            // По строке на каждый нуклид со своими сумм-пиками: образец узора
            // в ЕГО цвете и его имя. Узор в легенде обязателен — читатель ищет
            // на графике узор, а не текст; цвет обязателен — иначе при двух
            // нуклидах с каскадами непонятно, чья штриховка какая.
            // Доля не печатается нарочно: сумм-пики уже посчитаны внутри доли
            // своего нуклида, и второе число рядом складывали бы с первым.
            foreach (FsaStackLayer layer in this.FsaSumPeakLayers(result))
            {
                Color color = this.FsaColorOf(layer.Name);
                using (Brush swatch = new HatchBrush(HatchStyle.DarkUpwardDiagonal,
                                                     FsaSumPeakHatchColor(color), color))
                {
                    g.FillRectangle(swatch, r.Left, r.Top + 4, 10, 8);
                }

                g.DrawString(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                           Resources.FSASumPeakRow,
                                           FsaPalette.DisplayName(layer.Name)),
                             this.Font, Brushes.Black, nameRect);
                r.Y += FsaTableRowHeight;
                nameRect.Y += FsaTableRowHeight;
            }

            // «Не обнаружен» с пределом обнаружения (S9): имя серым — у
            // кандидата нет ленты и нет цвета, чёрное имя читалось бы как
            // строка состава; справа «< доля» той же колонкой и той же мерой,
            // что доли состава (S68). Формат G3 — три значащие цифры, точность
            // пределов выше трёх цифр была бы враньём.
            foreach (FsaCharacteristicLimit limit in FsaUndetectedNamed(result))
            {
                g.DrawString(FsaPalette.DisplayName(limit.Name), this.Font, Brushes.Gray, nameRect);
                g.DrawString(FsaLimitText(FsaLimitSharePercent(result, limit.DetectionLimitPeakCounts)),
                             this.Font, Brushes.Gray, r, this.farFormat);
                r.Y += FsaTableRowHeight;
                nameRect.Y += FsaTableRowHeight;
            }

            // (S69) Кандидаты, которые АПРИОРИ не могут показать себя гаммой, —
            // одной строкой с суммарным пределом. Их предел не ограничивает
            // содержание ничем, и печатать его отдельным числом на каждого
            // значило бы выдавать за измерение то, что измерением не является:
            // у Po-216 гамма одна, 804.9 кэВ с выходом 0.0019 %, у Po-212
            // излучений в таблице нет вовсе.
            //
            // ⛔ Это ВТОРАЯ «прочие» в таблице, и она НЕ ТА, что в составе
            // (S71): та сворачивает ОБНАРУЖЕННЫХ сверх лимита названных, эта —
            // НЕ обнаруженных ниже порога выхода. Поэтому у неё своя подпись со
            // своим числом свёрнутых имён — сложить два разных числа читатель
            // не должен даже случайно.
            //
            // ⚠ Сумма пределов как верхняя граница законна (если каждое
            // a_i < L_i, то Σa_i < ΣL_i), но доверительный уровень у суммы уже
            // не 95 %, и подпись этого не обещает.
            List<FsaCharacteristicLimit> folded = FsaUndetectedFolded(result);
            if (folded.Count > 0)
            {
                double sum = 0.0;
                foreach (FsaCharacteristicLimit limit in folded)
                {
                    if (!double.IsNaN(limit.DetectionLimitPeakCounts))
                    {
                        sum += limit.DetectionLimitPeakCounts;
                    }
                }

                g.DrawString(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                           Resources.FSAUndetectedFoldedRow, folded.Count),
                             this.Font, Brushes.Gray, nameRect);
                g.DrawString(FsaLimitText(FsaLimitSharePercent(result, sum)),
                             this.Font, Brushes.Gray, r, this.farFormat);
                r.Y += FsaTableRowHeight;
                nameRect.Y += FsaTableRowHeight;
            }

            // «БЕЗ ФОНА» (S44, решение Amber 15.08.2026) — ОТДЕЛЬНОЙ строкой и
            // красным, а не хвостом служебной пометки: в строке качества она
            // не помещалась в ширину таблицы и налезала на само χ², а сказать
            // это надо ЗАМЕТНО. Условие — по факту вычитания: фона не подали
            // вовсе или подали и отбросили (обрезан по каналам, нет времени) —
            // для читающего разницы нет, разбор в обоих случаях идёт по
            // неочищенному спектру. Молчание здесь уже стоило одиннадцати
            // спектров корпуса, разобранных без фона так, что никто не видел.
            if (!result.BackgroundUsed)
            {
                g.DrawString(Resources.FSANoBackgroundMark, this.Font, Brushes.Firebrick, nameRect);
                r.Y += FsaTableRowHeight;
                nameRect.Y += FsaTableRowHeight;
            }

            // (S78) Образы, построенные и предъявленные фиту, но не дожившие до
            // отчёта, — СВОЕЙ строкой, серым, БЕЗ ЧИСЛА (решение Amber
            // 18.08.2026: «числа состава ему не давать, доля у него ноль»).
            // Строка отдельная, а не хвост строки качества: приписанная к χ²
            // пометка не помещалась в ширину таблицы и вылезала за подложку —
            // ровно то, чем `S44` уже платила однажды.
            //
            // Молчание тут стоило дорого дважды. `S49`: `Ann-511` с 70 542
            // пиковыми отсчётами печаталась нулём и выглядела отсутствующей.
            // `S78`: на чароите ни `Backscatter`, ни `Esc-Cs`, ни рентген иода
            // при кристалле CsI — а построены были все, просто отсев по
            // значимости (`FsaAnalyzer.RefitZ`, умолчание 3) убирает колонку из
            // результата целиком, вместе со следом.
            if (result.SuppressedImages != null && result.SuppressedImages.Count > 0)
            {
                g.DrawString(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                           Resources.FSASuppressedMark,
                                           string.Join(", ", FsaSuppressedNames(result).ToArray())),
                             this.Font, Brushes.Gray, nameRect);
                r.Y += FsaTableRowHeight;
                nameRect.Y += FsaTableRowHeight;
            }

            // (S51) НЕВЯЗКА МОДЕЛИ — первой строкой, потому что читать надо её,
            // а не χ²/ndf. χ²/ndf между спектрами несравним: он растёт со
            // статистикой, и оба его вранья измерены по корпусу — `G1S16_Y88_P25`
            // с χ²/ndf 2.6 выглядит отличным разбором при невязке 65 % (спектр
            // тощий, шум прячет ошибку), а `ASN16_Th232` с χ²/ndf 743 выглядит
            // провалом при невязке 23 % (спектр жирный, модель средняя). Доля
            // формы, которую модель не описывает, читается одинаково на германии
            // и на обсидиане: 0 — модель согласна со статистикой, 100 % — не
            // объясняет ничего.
            g.DrawString(Resources.FSAModelResidualRow, this.Font, Brushes.Black, nameRect);
            g.DrawString(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                       Resources.FSAModelResidualValue,
                                       (100.0 * result.ModelResidual).ToString("n1",
                                           System.Globalization.CultureInfo.CurrentCulture)),
                         this.Font, Brushes.Black, r, this.farFormat);
            r.Y += FsaTableRowHeight;
            nameRect.Y += FsaTableRowHeight;

            string quality = "χ²/ndf";
            // Пометка S2: с матрицей отклика образы или без — всегда, одна из
            // двух. Молчать нельзя: матрица бракуется по отпечатку и формату
            // файла без единого сообщения, и «без матрицы» иначе неотличимо.
            quality += result.ResponseMatrixUsed
                ? Resources.FSAMatrixMark
                : Resources.FSANoMatrixMark;

            // Каскадное суммирование отмечается только когда оно СРАБОТАЛО:
            // у состава без каскадов (Cs-137, K-40) поправка возвращает
            // единицы, и пометка сказала бы о работе, которой не было.
            if (result.CascadeSummingUsed)
            {
                quality += Resources.FSACascadeMark;
            }

            if (!result.EfficiencyUsed)
            {
                quality += Resources.FSANoEfficiencyMark;
            }


            if (result.DriftOnGridEdge)
            {
                quality += Resources.FSADriftEdgeMark;
            }

            g.DrawString(quality, this.Font, Brushes.Black, r);
            g.DrawString(result.Chi2Ndf.ToString("n2"), this.Font, Brushes.Black, r, this.farFormat);
        }

        /// <summary>
        /// Таблица состава: тот же вид, что у панелей значений курсора —
        /// тень, белая заливка, чёрная рамка, те же строки.
        /// </summary>
        void DrawFsaOwnTable(Graphics g, int x, int y, int width)
        {
            // Один снимок на кадр: фон публикует результат в любой момент, и
            // таблица, размеченная по одному состоянию, заполняется по нему же,
            // а не по успевшему смениться.
            FsaResult result = this.fsaOverlay.Result;
            string status = this.fsaOverlay.Status;
            int rows = this.FsaTableRowCount(result, status);
            if (rows == 0)
            {
                return;
            }

            // Сообщение о состоянии переносится по ширине панели на несколько
            // строк — подложка обязана вместить фактическую высоту текста, а не
            // одну табличную строку.
            int height = result == null
                ? (int)Math.Ceiling(g.MeasureString(status, this.Font, width - 12).Height) + 8
                : rows * FsaTableRowHeight + 8;
            g.FillRectangle(Brushes.DarkGray, x + 3, y + 3, width, height);
            g.FillRectangle(Brushes.White, x, y, width, height);
            g.DrawRectangle(Pens.Black, x, y, width, height);

            Rectangle r = new Rectangle(x + 8, y + 4, width - 12, height - 8);
            this.DrawFsaRows(g, r, result, status);
        }
    }
}
