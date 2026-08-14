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
        const int FsaMaxNamedLayers = 6;

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

            // +1 — строка качества, плюс по строке на каждый слой со своими
            // сумм-пиками, плюс по строке «< МДА» на каждого НЕвошедшего
            // кандидата (S9, решение Amber 14.08.2026 «показывай»).
            return this.GetFsaLayers(result).Count + 1 + this.FsaSumPeakLayers(result).Count
                   + FsaUndetected(result).Count;
        }

        /// <summary>
        /// НЕ вошедшие в состав кандидаты библиотеки с определённым пределом
        /// обнаружения (S9). Строка «Cs-137 &lt; 192 имп/с» — метрологический
        /// ответ «не обнаружен»: без МДА он полответа, потому что «не нашли»
        /// и «не могли найти» — разные вещи. Вырожденные и без МДА не
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
            // строка состава; справа «< МДА» в имп/с, той же колонкой, что
            // доли. Формат G3 — три значащие цифры, точность пределов выше
            // трёх цифр была бы враньём.
            foreach (FsaCharacteristicLimit limit in FsaUndetected(result))
            {
                g.DrawString(FsaPalette.DisplayName(limit.Name), this.Font, Brushes.Gray, nameRect);
                g.DrawString(string.Format(System.Globalization.CultureInfo.CurrentCulture,
                                           Resources.FSAMdaValue,
                                           limit.DetectionLimitRate.ToString("G3",
                                               System.Globalization.CultureInfo.CurrentCulture)),
                             this.Font, Brushes.Gray, r, this.farFormat);
                r.Y += FsaTableRowHeight;
                nameRect.Y += FsaTableRowHeight;
            }

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
