using System;

namespace BecquerelMonitor
{
    /// <summary>
    /// (ПРОТОТИП, задача Amber 26.10.2026) СЖАТИЕ ОСИ X — СУММА КАНАЛОВ СТОЛБЦА.
    ///
    /// Когда на экранный столбец приходится больше одного канала (масштаб &lt; 1
    /// пикселя на канал), столбец рисует не один канал под левым краем, а
    /// СУММУ всех каналов, которые он накрывает, — площадь пика сохраняется при
    /// любом масштабе. Решения Amber 26.10.2026 вопросником, дословно:
    /// «Долями по перекрытию» (канал на границе столбца делится между соседями
    /// пропорционально перекрытию — без гребёнки 8/9 каналов),
    /// «Диапазон и сумма группы» (панель курсора в сжатом виде),
    /// «До / после / контроль 1024» (снимки приёмки).
    ///
    /// Канал i занимает на оси каналов полуинтервал [i − ½, i + ½) — так же, как
    /// его рисует главный график (`AMBER88`: канал i стоит в ChannelToEnergy(i)).
    /// Столбец x занимает [x, x + 1) экрана; его края переводятся в дробные
    /// каналы ТЕМИ ЖЕ формулами, что и прежняя карта «пиксель → канал».
    ///
    /// При одном канале на столбец и меньше (увеличение, 1:1) путь прежний —
    /// ровно тот канал, что брался и раньше; правка этот кадр не меняет.
    /// </summary>
    public partial class EnergySpectrumView
    {
        /// <summary>
        /// Порог сжатия: столбец считается суммой, только если накрывает БОЛЬШЕ
        /// одного канала. Допуск — чтобы ровно 1:1 (края на целых каналах) не
        /// превращался в смесь двух соседей.
        /// </summary>
        const double CompressedSpanThreshold = 1.0 + 1e-9;

        /// <summary>
        /// Дробный канал спектра на левой кромке экранной координаты <paramref name="x"/>
        /// — те же формулы, что у <see cref="ChannelAtPixel"/> и <c>DrawBarChart</c>,
        /// без округления.
        /// </summary>
        double EdgeChannel(double x, EnergySpectrum spectrum, EnergyCalibration calibration, bool isBackground)
        {
            double offset = (x - this.scrollX - this.left) / this.horizontalScale;
            if (this.horizontalUnit == HorizontalUnit.Energy)
            {
                return PolynomialEnergyCalibration.ChannelOf(calibration, offset / this.pixelPerEnergy + this.energyViewOffset, spectrum.NumberOfChannels);
            }

            if (isBackground && this.backgroundEnergyCalibration != null && this.backgroundEnergySpectrum != null
                && !this.baseEnergyCalibration.Equals(this.backgroundEnergyCalibration))
            {
                return PolynomialEnergyCalibration.ChannelOf(this.backgroundEnergyCalibration, this.baseEnergyCalibration.ChannelToEnergy(offset), this.backgroundEnergySpectrum.NumberOfChannels);
            }

            return offset;
        }

        /// <summary>Каналы, накрытые столбцом <paramref name="pixel"/>: [<paramref name="from"/>, <paramref name="to"/>).</summary>
        void PixelSpan(int pixel, EnergySpectrum spectrum, EnergyCalibration calibration, bool isBackground, out double from, out double to)
        {
            from = this.EdgeChannel(pixel, spectrum, calibration, isBackground);
            to = this.EdgeChannel(pixel + 1, spectrum, calibration, isBackground);
            if (to < from)
            {
                double t = from;
                from = to;
                to = t;
            }
        }

        static bool IsCompressedSpan(double from, double to)
        {
            return to - from > CompressedSpanThreshold;
        }

        /// <summary>
        /// Сумма <paramref name="values"/> по [<paramref name="from"/>, <paramref name="to"/>)
        /// с долями по перекрытию: канал i весит долю своего [i − ½, i + ½),
        /// попавшую в отрезок. Сумма по смежным отрезкам равна сумме каналов —
        /// площадь сохраняется точно.
        /// </summary>
        static double OverlapSum(double[] values, double from, double to)
        {
            if (values == null || !(to > from))
            {
                return 0.0;
            }

            int first = Math.Max(0, (int)Math.Floor(from + 0.5));
            int last = Math.Min(values.Length - 1, (int)Math.Ceiling(to + 0.5) - 1);
            double sum = 0.0;
            for (int i = first; i <= last; i++)
            {
                double w = Math.Min(to, i + 0.5) - Math.Max(from, i - 0.5);
                if (w > 0.0)
                {
                    sum += values[i] * w;
                }
            }

            return sum;
        }

        /// <summary>То же для сырых отсчётов (панель курсора читает <c>Spectrum</c>).</summary>
        static double OverlapSum(int[] values, double from, double to)
        {
            if (values == null || !(to > from))
            {
                return 0.0;
            }

            int first = Math.Max(0, (int)Math.Floor(from + 0.5));
            int last = Math.Min(values.Length - 1, (int)Math.Ceiling(to + 0.5) - 1);
            double sum = 0.0;
            for (int i = first; i <= last; i++)
            {
                double w = Math.Min(to, i + 0.5) - Math.Max(from, i - 0.5);
                if (w > 0.0)
                {
                    sum += values[i] * w;
                }
            }

            return sum;
        }

        /// <summary>
        /// Значение массива по каналам ОСНОВНОГО спектра, нарисованное в столбце
        /// <paramref name="pixel"/>: сумма при сжатии, иначе канал прежней карты.
        /// <c>false</c> — под столбцом нет канала.
        /// </summary>
        bool TryColumnValue(double[] values, int pixel, out double value)
        {
            value = 0.0;
            if (values == null || this.energySpectrum == null || this.energyCalibration == null)
            {
                return false;
            }

            int channel = this.ChannelAtPixel(this.energySpectrum, this.energyCalibration, pixel);
            if (channel < 0 || channel >= values.Length)
            {
                return false;
            }

            double from, to;
            this.PixelSpan(pixel, this.energySpectrum, this.energyCalibration, false, out from, out to);
            value = IsCompressedSpan(from, to) ? OverlapSum(values, from, to) : values[channel];
            return true;
        }

        /// <summary>
        /// Сумма каналов столбца <paramref name="pixel"/> основного спектра, только
        /// если столбец сжат; иначе <c>false</c> — вызывающий берёт свой канал, как прежде.
        /// </summary>
        bool TryCompressedColumnValue(double[] values, int pixel, out double value)
        {
            value = 0.0;
            if (values == null || this.energySpectrum == null || this.energyCalibration == null)
            {
                return false;
            }

            double from, to;
            this.PixelSpan(pixel, this.energySpectrum, this.energyCalibration, false, out from, out to);
            if (!IsCompressedSpan(from, to))
            {
                return false;
            }

            value = OverlapSum(values, from, to);
            return true;
        }

        /// <summary>
        /// Значение по карте кадра (<see cref="EnsurePixelChannelMap"/>): индекс
        /// <paramref name="index"/> — столбец от первого в карте.
        /// </summary>
        double MappedColumnValue(double[] values, int index, int channel)
        {
            double from = this.pixelSpanFrom[index];
            double to = this.pixelSpanTo[index];
            return IsCompressedSpan(from, to) ? OverlapSum(values, from, to) : values[channel];
        }

        /// <summary>Сжат ли столбец карты кадра.</summary>
        bool IsMappedColumnCompressed(int index)
        {
            return IsCompressedSpan(this.pixelSpanFrom[index], this.pixelSpanTo[index]);
        }

        /// <summary>
        /// Для подгонки вертикали: значение, которое примет столбец с центром в
        /// канале <paramref name="k"/> основного спектра. При сжатии — сумма по окну
        /// шириной в «каналов на столбец» вокруг k; иначе сам канал.
        /// <paramref name="spectrum"/>/<paramref name="calibration"/> — чей массив
        /// (фон переводится через энергию).
        /// </summary>
        double BoundaryValue(double[] values, int k, EnergySpectrum spectrum, EnergyCalibration calibration)
        {
            double width = this.ChannelsPerPixelAt(k);
            if (!(width > CompressedSpanThreshold))
            {
                int ch = k;
                if (calibration != null && this.energyCalibration != null && !ReferenceEquals(calibration, this.energyCalibration)
                    && !calibration.Equals(this.energyCalibration))
                {
                    ch = NearestChannel(PolynomialEnergyCalibration.ChannelOf(calibration, this.energyCalibration.ChannelToEnergy(k), spectrum.NumberOfChannels));
                }
                return ch >= 0 && ch < values.Length ? values[ch] : 0.0;
            }

            double from = k - width / 2.0;
            double to = k + width / 2.0;
            if (calibration != null && this.energyCalibration != null && !ReferenceEquals(calibration, this.energyCalibration)
                && !calibration.Equals(this.energyCalibration))
            {
                from = PolynomialEnergyCalibration.ChannelOf(calibration, this.energyCalibration.ChannelToEnergy(from), spectrum.NumberOfChannels);
                to = PolynomialEnergyCalibration.ChannelOf(calibration, this.energyCalibration.ChannelToEnergy(to), spectrum.NumberOfChannels);
            }
            return OverlapSum(values, from, to);
        }

        /// <summary>
        /// Для подгонки вертикали, массив в каналах основного спектра: при сжатии
        /// сумма по окну «каналов на столбец» вокруг <paramref name="k"/>, иначе
        /// сам канал (побитово прежнее).
        /// </summary>
        double WindowValue(double[] values, int k)
        {
            double width = this.ChannelsPerPixelAt(k);
            if (!(width > CompressedSpanThreshold))
            {
                return values[k];
            }

            return OverlapSum(values, k - width / 2.0, k + width / 2.0);
        }

        /// <summary>Сжат ли столбец около канала <paramref name="k"/> основного спектра.</summary>
        bool IsCompressedAt(int k)
        {
            return this.ChannelsPerPixelAt(k) > CompressedSpanThreshold;
        }

        /// <summary>Каналов основного спектра на экранный столбец около канала <paramref name="k"/>.</summary>
        double ChannelsPerPixelAt(double k)
        {
            if (!(this.horizontalScale > 0.0))
            {
                return 1.0;
            }

            if (this.horizontalUnit != HorizontalUnit.Energy || this.energyCalibration == null)
            {
                return 1.0 / this.horizontalScale;
            }

            double dE = this.energyCalibration.ChannelToEnergy(k + 0.5) - this.energyCalibration.ChannelToEnergy(k - 0.5);
            double pixels = Math.Abs(dE) * this.pixelPerEnergy * this.horizontalScale;
            return pixels > 0.0 ? 1.0 / pixels : 1.0;
        }

        /// <summary>
        /// Сжат ли вид для спектра: столбец в середине видимого поля накрывает
        /// больше канала. Ломаные решают это одним разом на кривую — смесь
        /// «по каналам» и «по столбцам» в одной ломаной не рисуется.
        /// </summary>
        bool IsViewCompressed(EnergySpectrum spectrum, EnergyCalibration calibration, bool isBackground, out int firstPixel, out int lastPixel)
        {
            int rightEdge = this.CalcMaximumXValue() + this.scrollX + this.left;
            firstPixel = Math.Max(this.VisibleLeftPixel, this.left + 1);
            lastPixel = Math.Min(this.VisibleRightPixel, rightEdge - 1);
            if (firstPixel > lastPixel || spectrum == null || calibration == null)
            {
                return false;
            }

            double from, to;
            this.PixelSpan((firstPixel + lastPixel) / 2, spectrum, calibration, isBackground, out from, out to);
            return IsCompressedSpan(from, to);
        }

        readonly System.Collections.Generic.List<System.Drawing.Point> compressedLineBuffer = new System.Collections.Generic.List<System.Drawing.Point>();

        /// <summary>
        /// <c>DrawLineChart</c> при сжатии: точка на столбец, значение — сумма
        /// каналов столбца. <c>false</c> — вид не сжат, рисует прежний путь.
        /// </summary>
        bool DrawCompressedLineChart(System.Drawing.Graphics g, System.Drawing.Pen pen, EnergySpectrum spectrum, EnergyCalibration calibration, bool isBackground)
        {
            int firstPixel, lastPixel;
            if (!this.IsViewCompressed(spectrum, calibration, isBackground, out firstPixel, out lastPixel))
            {
                return false;
            }

            this.compressedLineBuffer.Clear();
            for (int x = firstPixel; x <= lastPixel; x++)
            {
                double from, to;
                this.PixelSpan(x, spectrum, calibration, isBackground, out from, out to);
                if (!(to > from))
                {
                    continue;
                }

                double value = OverlapSum(spectrum.DrawingSpectrum, from, to);
                if (isBackground)
                {
                    if (this.verticalUnit == VerticalUnit.Counts)
                    {
                        if (spectrum.EffectiveLiveTime != 0.0)
                        {
                            value = value * this.energySpectrum.EffectiveLiveTime / spectrum.EffectiveLiveTime;
                        }
                    }
                    else if (spectrum.EffectiveLiveTime != 0.0)
                    {
                        value /= spectrum.EffectiveLiveTime;
                    }
                }
                else if (this.verticalUnit == VerticalUnit.CountsPerSecond && spectrum.EffectiveLiveTime != 0.0)
                {
                    value /= spectrum.EffectiveLiveTime;
                }

                this.compressedLineBuffer.Add(new System.Drawing.Point(x, this.GetSpectrumValueY(value)));
            }

            this.FlushCompressedLine(g, pen);
            return true;
        }

        /// <summary>
        /// Контур пика (<c>DrawPeakLineChart</c>) при сжатии: столбцы, задевающие
        /// каналы пика; верх — подложка по всему столбцу плюс пик по своим каналам.
        /// </summary>
        bool DrawCompressedPeakLine(System.Drawing.Graphics g, System.Drawing.Pen pen, EnergySpectrum spectrum, EnergyCalibration calibration, double[] peakSpectrum, int minChannel, int maxChannel)
        {
            int firstPixel, lastPixel;
            if (!this.IsViewCompressed(spectrum, calibration, false, out firstPixel, out lastPixel))
            {
                return false;
            }

            this.compressedLineBuffer.Clear();
            for (int x = firstPixel; x <= lastPixel; x++)
            {
                double from, to;
                this.PixelSpan(x, spectrum, calibration, false, out from, out to);
                if (!(to > minChannel - 0.5) || !(from < maxChannel - 0.5))
                {
                    continue;
                }

                double value = OverlapSum(spectrum.DrawingSpectrum, from, to)
                               + OverlapSum(peakSpectrum, Math.Max(from, minChannel - 0.5), Math.Min(to, maxChannel - 0.5));
                if (this.verticalUnit == VerticalUnit.CountsPerSecond && spectrum.EffectiveLiveTime != 0.0)
                {
                    value /= spectrum.EffectiveLiveTime;
                }

                this.compressedLineBuffer.Add(new System.Drawing.Point(x, this.GetSpectrumValueY(value)));
            }

            this.FlushCompressedLine(g, pen);
            return true;
        }

        void FlushCompressedLine(System.Drawing.Graphics g, System.Drawing.Pen pen)
        {
            try
            {
                if (this.compressedLineBuffer.Count >= 2)
                {
                    g.DrawLines(pen, this.compressedLineBuffer.ToArray());
                }
                else if (this.compressedLineBuffer.Count == 1)
                {
                    System.Drawing.Point only = this.compressedLineBuffer[0];
                    g.DrawLine(pen, only, only);
                }
            }
            catch (Exception)
            {
                // Те же плохие координаты (NaN/бесконечность при испорченной
                // калибровке), что глотает прежний путь DrawLineChart.
            }
            this.compressedLineBuffer.Clear();
        }

        /// <summary>
        /// Каналы, накрытые столбцом курсора, для панели курсора: целые края
        /// [<paramref name="first"/>, <paramref name="last"/>] и дробный отрезок.
        /// <c>false</c> — столбец не сжат (панель показывает один канал, как прежде).
        /// </summary>
        bool TryCursorSpan(out double from, out double to, out int first, out int last)
        {
            from = to = 0.0;
            first = last = -1;
            if (this.energySpectrum == null || this.energyCalibration == null || this.cursorX < 0)
            {
                return false;
            }

            this.PixelSpan(this.cursorX, this.energySpectrum, this.energyCalibration, false, out from, out to);
            if (!IsCompressedSpan(from, to))
            {
                return false;
            }

            int n = this.energySpectrum.NumberOfChannels;
            first = Math.Max(0, (int)Math.Floor(from + 0.5));
            last = Math.Min(n - 1, (int)Math.Ceiling(to + 0.5) - 1);
            return first <= last;
        }
    }
}
