using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Чертёж геометрии: осевой разрез, торец детектора кверху, источник над
    /// ним — та же раскладка, что в конструкторе геометрий LSRM, чтобы числа в
    /// полях читались привычно.
    ///
    /// Чертёж не украшение: без него из двадцати полей не видно, что за что
    /// отвечает, а ошибка в размере не выглядит ошибкой — расчёт честно
    /// доводится до конца и выдаёт кривую не той геометрии.
    ///
    /// Рисуется ровно то, что потом соберёт <see cref="EfficiencySimulator"/>:
    /// прямоугольный кристалл показан прямоугольным (формат `.in` этого не
    /// умеет, а мы умеем), оправа стоит ЗА кристаллом, стенки сосуда и проба
    /// разведены цветом. Масштаб общий по обеим осям — пропорции честные.
    /// </summary>
    public sealed class GeometrySketch : Control
    {
        public enum SketchMode
        {
            Detector,
            Source
        }

        // Палитра взята с чертежа GMaster, чтобы слои узнавались с первого
        // взгляда теми, кто уже работал с их конструктором.
        static readonly Color Canvas = Color.FromArgb(0xD6, 0xD2, 0xC4);
        static readonly Color CrystalColor = Color.FromArgb(0x35, 0xA5, 0xAD);
        static readonly Color ReflectorColor = Color.FromArgb(0x8C, 0xEC, 0xEC);
        static readonly Color CladdingColor = Color.FromArgb(0x82, 0x90, 0xB0);
        static readonly Color WallColor = Color.FromArgb(0xA6, 0xD5, 0xE8);
        static readonly Color SampleColor = Color.FromArgb(0x78, 0x80, 0x8E);
        static readonly Color Ink = Color.FromArgb(0x20, 0x20, 0x20);

        GeometryModel model;

        public GeometrySketch()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Canvas;
        }

        public SketchMode Mode { get; set; }

        public void SetModel(GeometryModel value)
        {
            this.model = value;
            this.Invalidate();
        }

        // ------------------------------------------------------------------
        // Мир -> экран
        // ------------------------------------------------------------------

        double scale, worldLeft, worldTop;
        int padLeft, padTop;

        float X(double x)
        {
            return (float)(this.padLeft + (x - this.worldLeft) * this.scale);
        }

        float Y(double z)
        {
            return (float)(this.padTop + (z - this.worldTop) * this.scale);
        }

        float L(double length)
        {
            return (float)(length * this.scale);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Canvas);

            using (Pen frame = new Pen(Color.FromArgb(0x90, 0x90, 0x90)))
            {
                g.DrawRectangle(frame, 0, 0, this.Width - 1, this.Height - 1);
            }

            GeometryModel m = this.model;
            if (m == null)
            {
                return;
            }

            // Размеры детектора. У бруска в разрезе виден торец X; Y уходит в
            // глубину и подписывается отдельно, иначе разрез врал бы о форме.
            double halfWidth = m.Shape == CrystalShape.Box
                ? 0.5 * Math.Max(m.CrystalBoxX, 0.0)
                : 0.5 * Math.Max(m.CrystalDiameter, 0.0);
            double height = m.Shape == CrystalShape.Box
                ? Math.Max(m.CrystalBoxZ, 0.0)
                : Math.Max(m.CrystalHeight, 0.0);
            double tfr = Math.Max(m.FrontReflectorThickness, 0.0);
            double tsr = Math.Max(m.SideReflectorThickness, 0.0);
            double tfc = Math.Max(m.FrontCladdingThickness, 0.0);
            double tsc = Math.Max(m.SideCladdingThickness, 0.0);
            double tm = Math.Max(m.MountingThickness, 0.0);
            if (!(halfWidth > 0.0) || !(height > 0.0))
            {
                return;
            }

            double outerHalf = halfWidth + tsr + tsc;
            double zFace = -(tfr + tfc);
            double zBack = height + tm;

            double left = -outerHalf, right = outerHalf, top = zFace, bottom = zBack;
            if (this.Mode == SketchMode.Source)
            {
                double sl, sr, st, sb;
                this.SourceBounds(m, zFace, out sl, out sr, out st, out sb);
                left = Math.Min(left, sl);
                right = Math.Max(right, sr);
                top = Math.Min(top, st);
                bottom = Math.Max(bottom, sb);
            }

            // Поля под размерные линии: слева и сверху они длиннее, там стоят
            // выноски с числами.
            const int MarginX = 74, MarginTop = 34, MarginBottom = 30;
            double worldWidth = Math.Max(right - left, 1e-6);
            double worldHeight = Math.Max(bottom - top, 1e-6);
            double sx = (this.Width - 2.0 * MarginX) / worldWidth;
            double sy = (this.Height - MarginTop - MarginBottom) / worldHeight;
            this.scale = Math.Min(sx, sy);
            if (!(this.scale > 0.0) || double.IsInfinity(this.scale))
            {
                return;
            }

            this.worldLeft = left;
            this.worldTop = top;
            this.padLeft = (int)((this.Width - worldWidth * this.scale) / 2.0);
            this.padTop = MarginTop + (int)((this.Height - MarginTop - MarginBottom
                                             - worldHeight * this.scale) / 2.0);

            if (this.Mode == SketchMode.Source)
            {
                this.DrawSource(g, m, zFace);
            }

            this.DrawDetector(g, m, halfWidth, height, tfr, tsr, tfc, tsc, tm);
            if (this.Mode == SketchMode.Detector)
            {
                this.Annotate(g, m, halfWidth, height, tfr, tsr, tfc, tsc, tm);
            }
            else
            {
                this.AnnotateSource(g, m, zFace);
            }
        }

        // ------------------------------------------------------------------
        // Детектор
        // ------------------------------------------------------------------

        void DrawDetector(Graphics g, GeometryModel m, double halfWidth, double height,
                          double tfr, double tsr, double tfc, double tsc, double tm)
        {
            double outerHalf = halfWidth + tsr + tsc;
            double zFace = -(tfr + tfc);

            // Слои рисуются снаружи внутрь: корпус целиком, потом отражатель,
            // потом кристалл. Так же они и вложены в сцене расчёта — там
            // побеждает первая область, в которую попала точка.
            Fill(g, CladdingColor, -outerHalf, zFace, 2.0 * outerHalf, height + tm - zFace);
            double reflHalf = halfWidth + tsr;
            Fill(g, ReflectorColor, -reflHalf, -tfr, 2.0 * reflHalf, height + tfr);
            Fill(g, CrystalColor, -halfWidth, 0.0, 2.0 * halfWidth, height);

            using (Pen pen = new Pen(Ink, 1.2f))
            {
                Outline(g, pen, -outerHalf, zFace, 2.0 * outerHalf, height + tm - zFace);
                Outline(g, pen, -halfWidth, 0.0, 2.0 * halfWidth, height);
            }
        }

        void Annotate(Graphics g, GeometryModel m, double halfWidth, double height,
                      double tfr, double tsr, double tfc, double tsc, double tm)
        {
            double outerHalf = halfWidth + tsr + tsc;
            double zFace = -(tfr + tfc);

            using (Pen pen = new Pen(Ink, 1f))
            using (Brush ink = new SolidBrush(Ink))
            {
                // Поперечник кристалла — над торцом, длина — справа.
                this.DimH(g, pen, ink, -halfWidth, halfWidth, zFace - 0.18 * (height + 1.0), 2.0 * halfWidth);
                this.DimV(g, pen, ink, outerHalf + 0.12 * (2.0 * outerHalf + 1.0), 0.0, height, height);

                if (tfr > 0.0)
                {
                    this.DimV(g, pen, ink, -halfWidth * 0.45, -tfr, 0.0, tfr);
                }

                if (tfc > 0.0)
                {
                    this.DimV(g, pen, ink, halfWidth * 0.45, zFace, -tfr, tfc);
                }

                if (tsr > 0.0)
                {
                    this.DimH(g, pen, ink, -halfWidth - tsr, -halfWidth, height * 0.35, tsr);
                }

                if (tsc > 0.0)
                {
                    this.DimH(g, pen, ink, -outerHalf, -halfWidth - tsr, height * 0.62, tsc);
                }

                if (tm > 0.0)
                {
                    this.DimV(g, pen, ink, 0.0, height, height + tm, tm);
                }

                // У бруска третий размер в разрез не попадает — его надо
                // назвать словами, иначе чертёж выглядит как цилиндр.
                if (m.Shape == CrystalShape.Box)
                {
                    string text = string.Format(CultureInfo.InvariantCulture,
                        "Y = {0:G4} cm", m.CrystalBoxY);
                    g.DrawString(text, this.Font, ink, 6, 6);
                }
            }
        }

        // ------------------------------------------------------------------
        // Источник
        // ------------------------------------------------------------------

        void SourceBounds(GeometryModel m, double zFace,
                          out double left, out double right, out double top, out double bottom)
        {
            switch (m.SourceType)
            {
                case GeometrySourceType.Point:
                    left = -0.5;
                    right = 0.5;
                    top = zFace - Math.Max(m.PointDistance, 0.0);
                    bottom = zFace;
                    return;

                case GeometrySourceType.Cylinder:
                {
                    double rOut = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                    double zTop = zFace - Math.Max(m.BeakerToDetectorDistance, 0.0);
                    left = -rOut;
                    right = rOut;
                    top = zTop - Math.Max(m.BeakerHeight, 0.0) - Math.Max(m.SourceHeight, 0.0);
                    bottom = zFace;
                    return;
                }

                default:
                {
                    double rOut = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                    double zCeiling = zFace - Math.Max(m.MarinelliToDetectorDistance, 0.0);
                    double hs = Math.Max(m.MarinelliSourceHeight, 0.0);
                    double hh = Math.Max(m.MarinelliHoleHeight, 0.0);
                    left = -rOut;
                    right = rOut;
                    top = zCeiling - Math.Max(m.MarinelliEndWallThickness, 0.0) - Math.Max(hs - hh, 0.0);
                    bottom = Math.Max(zCeiling + Math.Max(m.MarinelliBeakerHeight, 0.0), zFace);
                    return;
                }
            }
        }

        void DrawSource(Graphics g, GeometryModel m, double zFace)
        {
            switch (m.SourceType)
            {
                case GeometrySourceType.Point:
                {
                    float x = this.X(0.0), y = this.Y(zFace - Math.Max(m.PointDistance, 0.0));
                    using (Brush b = new SolidBrush(SampleColor))
                    {
                        g.FillEllipse(b, x - 5f, y - 5f, 10f, 10f);
                    }

                    using (Pen pen = new Pen(Ink, 1f) { DashStyle = DashStyle.Dash })
                    {
                        g.DrawLine(pen, x, y, x, this.Y(zFace));
                    }

                    return;
                }

                case GeometrySourceType.Cylinder:
                {
                    double rOut = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                    double wall = Math.Max(m.BeakerSideWallThickness, 0.0);
                    double end = Math.Max(m.BeakerEndWallThickness, 0.0);
                    double hs = Math.Max(m.SourceHeight, 0.0);
                    double zWallTop = zFace - Math.Max(m.BeakerToDetectorDistance, 0.0);
                    double zSrcTop = zWallTop - end;
                    Fill(g, WallColor, -rOut, zSrcTop - hs, 2.0 * rOut, hs + end);
                    Fill(g, SampleColor, -(rOut - wall), zSrcTop - hs, 2.0 * (rOut - wall), hs);
                    using (Pen pen = new Pen(Ink, 1.2f))
                    {
                        Outline(g, pen, -rOut, zSrcTop - hs, 2.0 * rOut, hs + end);
                    }

                    return;
                }

                default:
                {
                    // Стакан Маринелли: проба охватывает детектор, колодец
                    // открыт снизу — детектор входит в него.
                    double rOut = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                    double rh = 0.5 * Math.Max(m.MarinelliHoleDiameter, 0.0);
                    double ths = Math.Max(m.MarinelliHoleSideThickness, 0.0);
                    double the = Math.Max(m.MarinelliHoleEndWallThickness, 0.0);
                    double side = Math.Max(m.MarinelliSideThickness, 0.0);
                    double hs = Math.Max(m.MarinelliSourceHeight, 0.0);
                    double hh = Math.Max(m.MarinelliHoleHeight, 0.0);
                    double zCeiling = zFace - Math.Max(m.MarinelliToDetectorDistance, 0.0);
                    double cap = Math.Max(0.0, hs - hh);
                    double zSrc0 = zCeiling - the - cap;
                    double body = Math.Max(m.MarinelliBeakerHeight, 0.0);

                    Fill(g, WallColor, -rOut, zSrc0 - side, 2.0 * rOut, body + side);
                    double rSrcOut = Math.Max(rh + ths, rOut - side);
                    Fill(g, SampleColor, -rSrcOut, zSrc0, 2.0 * rSrcOut, hs);
                    // колодец: вырез в пробе, стенка колодца и пустота внутри
                    Fill(g, WallColor, -(rh + ths), zCeiling - the, 2.0 * (rh + ths), the + hh);
                    Fill(g, Canvas, -rh, zCeiling, 2.0 * rh, hh);

                    using (Pen pen = new Pen(Ink, 1.2f))
                    {
                        Outline(g, pen, -rOut, zSrc0 - side, 2.0 * rOut, body + side);
                        Outline(g, pen, -rh, zCeiling, 2.0 * rh, hh);
                    }

                    return;
                }
            }
        }

        void AnnotateSource(Graphics g, GeometryModel m, double zFace)
        {
            using (Pen pen = new Pen(Ink, 1f))
            using (Brush ink = new SolidBrush(Ink))
            {
                switch (m.SourceType)
                {
                    case GeometrySourceType.Point:
                        this.DimV(g, pen, ink, 0.6, zFace - Math.Max(m.PointDistance, 0.0), zFace,
                                  Math.Max(m.PointDistance, 0.0));
                        return;

                    case GeometrySourceType.Cylinder:
                    {
                        double rOut = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                        double hs = Math.Max(m.SourceHeight, 0.0);
                        double zWallTop = zFace - Math.Max(m.BeakerToDetectorDistance, 0.0);
                        double zSrcTop = zWallTop - Math.Max(m.BeakerEndWallThickness, 0.0);
                        this.DimH(g, pen, ink, -rOut, rOut, zSrcTop - hs - 0.25 * (hs + 1.0), 2.0 * rOut);
                        this.DimV(g, pen, ink, rOut * 1.25, zSrcTop - hs, zSrcTop, hs);
                        this.DimV(g, pen, ink, 0.0, zWallTop, zFace,
                                  Math.Max(m.BeakerToDetectorDistance, 0.0));
                        return;
                    }

                    default:
                    {
                        double rOut = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                        double rh = 0.5 * Math.Max(m.MarinelliHoleDiameter, 0.0);
                        double hs = Math.Max(m.MarinelliSourceHeight, 0.0);
                        double hh = Math.Max(m.MarinelliHoleHeight, 0.0);
                        double the = Math.Max(m.MarinelliHoleEndWallThickness, 0.0);
                        double zCeiling = zFace - Math.Max(m.MarinelliToDetectorDistance, 0.0);
                        double cap = Math.Max(0.0, hs - hh);
                        double zSrc0 = zCeiling - the - cap;
                        this.DimH(g, pen, ink, -rOut, rOut, zSrc0 - 0.14 * (hs + 1.0), 2.0 * rOut);
                        this.DimH(g, pen, ink, -rh, rh, zCeiling + hh * 0.55, 2.0 * rh);
                        this.DimV(g, pen, ink, -rOut * 1.16, zSrc0, zSrc0 + hs, hs);
                        this.DimV(g, pen, ink, rh * 0.55, zCeiling, zCeiling + hh, hh);
                        return;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Примитивы
        // ------------------------------------------------------------------

        void Fill(Graphics g, Color color, double x, double z, double width, double height)
        {
            if (!(width > 0.0) || !(height > 0.0))
            {
                return;
            }

            using (Brush brush = new SolidBrush(color))
            {
                g.FillRectangle(brush, this.X(x), this.Y(z), this.L(width), this.L(height));
            }
        }

        void Outline(Graphics g, Pen pen, double x, double z, double width, double height)
        {
            if (!(width > 0.0) || !(height > 0.0))
            {
                return;
            }

            g.DrawRectangle(pen, this.X(x), this.Y(z), this.L(width), this.L(height));
        }

        /// <summary>Горизонтальный размер со стрелками и числом над линией.</summary>
        void DimH(Graphics g, Pen pen, Brush ink, double x1, double x2, double z, double value)
        {
            float a = this.X(x1), b = this.X(x2), y = this.Y(z);
            if (Math.Abs(b - a) < 2f)
            {
                return;
            }

            g.DrawLine(pen, a, y, b, y);
            Arrow(g, pen, a, y, +1f, 0f);
            Arrow(g, pen, b, y, -1f, 0f);
            string text = Format(value);
            SizeF size = g.MeasureString(text, this.Font);
            g.DrawString(text, this.Font, ink, (a + b) / 2f - size.Width / 2f, y - size.Height - 1f);
        }

        /// <summary>Вертикальный размер со стрелками и числом справа.</summary>
        void DimV(Graphics g, Pen pen, Brush ink, double x, double z1, double z2, double value)
        {
            float a = this.Y(z1), b = this.Y(z2), xx = this.X(x);
            if (Math.Abs(b - a) < 2f)
            {
                return;
            }

            g.DrawLine(pen, xx, a, xx, b);
            Arrow(g, pen, xx, a, 0f, +1f);
            Arrow(g, pen, xx, b, 0f, -1f);
            string text = Format(value);
            SizeF size = g.MeasureString(text, this.Font);
            g.DrawString(text, this.Font, ink, xx + 3f, (a + b) / 2f - size.Height / 2f);
        }

        static void Arrow(Graphics g, Pen pen, float x, float y, float dx, float dy)
        {
            const float S = 4f;
            if (dx != 0f)
            {
                g.DrawLine(pen, x, y, x + dx * S, y - S * 0.6f);
                g.DrawLine(pen, x, y, x + dx * S, y + S * 0.6f);
            }
            else
            {
                g.DrawLine(pen, x, y, x - S * 0.6f, y + dy * S);
                g.DrawLine(pen, x, y, x + S * 0.6f, y + dy * S);
            }
        }

        /// <summary>Число на выноске. Имя не Text: так называется свойство Control.</summary>
        static string Format(double value)
        {
            return value.ToString(value >= 10.0 ? "G4" : "G3", CultureInfo.InvariantCulture);
        }
    }
}
