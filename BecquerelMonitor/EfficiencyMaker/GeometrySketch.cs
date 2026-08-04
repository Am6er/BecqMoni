using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

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
            Source,

            /// <summary>
            /// Сводная миниатюра: детектор и образец на одном чертеже, из
            /// размеров — только габаритные.
            ///
            /// Двадцать выносок, осмысленные в редакторе, на миниатюре
            /// сливаются в кашу, поэтому размеры здесь названы СЛОВАМИ в углу:
            /// на маленьком поле подпись читается, а стрелка длиной в три
            /// точки — нет. Задача миниатюры одна — дать узнать конфигурацию,
            /// не открывая её.
            /// </summary>
            Overview
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

        string highlight;

        /// <summary>
        /// Ключ поля, размер которого сейчас подсвечен. Ставится по фокусу в
        /// поле: из двадцати чисел на чертеже без этого не понять, какое из них
        /// правишь, а подпись у тонкого слоя вдобавок стоит вплотную к соседней.
        /// </summary>
        public string HighlightKey
        {
            get
            {
                return this.highlight;
            }
            set
            {
                if (this.highlight != value)
                {
                    this.highlight = value;
                    this.Invalidate();
                }
            }
        }

        bool Lit(string key)
        {
            return key != null && string.Equals(key, this.highlight, StringComparison.Ordinal);
        }

        static readonly Color LitColor = Color.FromArgb(0xD0, 0x20, 0x20);

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

        // Выноски, стоящие ЗА пределами тела, задаются отступом в точках, а не в
        // сантиметрах. Отступ в сантиметрах пропорционален размеру детектора и
        // при длинном кристалле уносил выноску за край поля: размер X у бруска
        // 1.5x1.8x6.0 не рисовался вовсе, а у куба 2.54 рисовался — видимость
        // зависела от пропорций, что и есть худший вид ошибки в отрисовке.

        /// <summary>Мировая координата на N точек выше верха поля.</summary>
        double AboveTop(double pixels)
        {
            return this.worldTop - pixels / this.scale;
        }

        /// <summary>Мировая координата на N точек правее заданной.</summary>
        double RightOf(double x, double pixels)
        {
            return x + pixels / this.scale;
        }

        /// <summary>Мировая координата на N точек левее заданной.</summary>
        double LeftOf(double x, double pixels)
        {
            return x - pixels / this.scale;
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
            if (this.Mode != SketchMode.Detector)
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
            // Поля под выноски: сверху помещается размерная линия с подписью
            // (22 точки отступа плюс высота строки), по бокам — подпись с
            // числом.
            // Миниатюре широкие поля не нужны — выносок на ней нет, а место
            // дорого: чертёж и так мелкий.
            int MarginX = this.Mode == SketchMode.Overview ? 10 : 78;
            int MarginTop = this.Mode == SketchMode.Overview ? 8 : 42;
            int MarginBottom = this.Mode == SketchMode.Overview ? 8 : 30;
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

            if (this.Mode != SketchMode.Detector)
            {
                this.DrawSource(g, m, zFace);
            }

            this.DrawDetector(g, m, halfWidth, height, tfr, tsr, tfc, tsc, tm);
            if (this.Mode == SketchMode.Detector)
            {
                this.Annotate(g, m, halfWidth, height, tfr, tsr, tfc, tsc, tm);
            }
            else if (this.Mode == SketchMode.Source)
            {
                this.AnnotateSource(g, m, zFace);
            }
            else
            {
                this.AnnotateOverview(g, m);
            }
        }

        /// <summary>
        /// Подписи миниатюры: габариты детектора с названием формы и привязка
        /// образца. Всё словами, в левом верхнем углу, на подложке — иначе
        /// текст теряется на цветной заливке.
        /// </summary>
        void AnnotateOverview(Graphics g, GeometryModel m)
        {
            List<string> lines = new List<string>();
            lines.Add(m.Shape == CrystalShape.Box
                ? string.Format(CultureInfo.InvariantCulture, "{0}: {1:G4} x {2:G4} x {3:G4} cm",
                                Resources.EfficiencySketchBox, m.CrystalBoxX, m.CrystalBoxY, m.CrystalBoxZ)
                : string.Format(CultureInfo.InvariantCulture, "{0}: {1}{2:G4} x {3:G4} cm",
                                Resources.EfficiencySketchCylinder, "⌀", m.CrystalDiameter, m.CrystalHeight));

            switch (m.SourceType)
            {
                case GeometrySourceType.Point:
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:G4} cm",
                                            Resources.EfficiencySketchPoint,
                                            Math.Max(m.PointDistance, 0.0)));
                    break;

                case GeometrySourceType.Cylinder:
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1}{2:G4} x {3:G4} cm",
                                            Resources.EfficiencySketchBeaker, "⌀",
                                            Math.Max(m.BeakerDiameter, 0.0),
                                            Math.Max(m.SourceHeight, 0.0)));
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:G4} cm",
                                            Resources.EfficiencySketchDistance,
                                            Math.Max(m.BeakerToDetectorDistance, 0.0)));
                    break;

                default:
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1}{2:G4} x {3:G4} cm",
                                            Resources.EfficiencySketchMarinelli, "⌀",
                                            Math.Max(m.MarinelliBeakerDiameter, 0.0),
                                            Math.Max(m.MarinelliBeakerHeight, 0.0)));
                    break;
            }

            float width = 0f, lineHeight = 0f;
            foreach (string line in lines)
            {
                SizeF size = g.MeasureString(line, this.Font);
                width = Math.Max(width, size.Width);
                lineHeight = Math.Max(lineHeight, size.Height);
            }

            using (Brush plate = new SolidBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF)))
            using (Brush ink = new SolidBrush(Ink))
            {
                g.FillRectangle(plate, 4f, 4f, width + 8f, lineHeight * lines.Count + 6f);
                for (int i = 0; i < lines.Count; i++)
                {
                    g.DrawString(lines[i], this.Font, ink, 8f, 6f + i * lineHeight);
                }
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

            bool box = m.Shape == CrystalShape.Box;
            string widthKey = box ? "CrystalBoxX" : "CrystalDiameter";
            string lengthKey = box ? "CrystalBoxZ" : "CrystalHeight";

            using (Pen pen = new Pen(Ink, 1f))
            using (Brush ink = new SolidBrush(Ink))
            {
                // Поперечник кристалла — над торцом, длина — справа. Обе стоят
                // за телом детектора, поэтому отступ в точках.
                this.DimH(g, pen, ink, -halfWidth, halfWidth, this.AboveTop(22),
                          2.0 * halfWidth, widthKey);
                this.DimV(g, pen, ink, this.RightOf(outerHalf, 26), 0.0, height,
                          height, lengthKey);
                this.DimV(g, pen, ink, -halfWidth * 0.45, -tfr, 0.0, tfr, "FrontReflectorThickness");
                this.DimV(g, pen, ink, halfWidth * 0.45, zFace, -tfr, tfc, "FrontCladdingThickness");
                this.DimH(g, pen, ink, -halfWidth - tsr, -halfWidth, height * 0.35, tsr,
                          "SideReflectorThickness");
                this.DimH(g, pen, ink, -outerHalf, -halfWidth - tsr, height * 0.62, tsc,
                          "SideCladdingThickness");
                this.DimV(g, pen, ink, 0.0, height, height + tm, tm, "MountingThickness");

                // У бруска третий размер в разрез не попадает — его надо
                // назвать словами, иначе чертёж выглядит как цилиндр.
                if (box)
                {
                    string text = string.Format(CultureInfo.InvariantCulture,
                        "Y = {0:G4} cm", m.CrystalBoxY);
                    bool lit = this.Lit("CrystalBoxY");
                    using (Brush litInk = lit ? new SolidBrush(LitColor) : null)
                    {
                        g.DrawString(text, this.Font, lit ? litInk : ink, 6, 6);
                    }
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
                    // Границы обязаны совпадать с тем, что рисует DrawSource,
                    // иначе тело уезжает за край поля. Верх стакана — донышко
                    // под пробой: zSrc0 - endWall, где zSrc0 отсчитан от потолка
                    // колодца через ЕГО стенку (the), а не через донышко.
                    double rOut = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                    double zCeiling = zFace - Math.Max(m.MarinelliToDetectorDistance, 0.0);
                    double hs = Math.Max(m.MarinelliSourceHeight, 0.0);
                    double hh = Math.Max(m.MarinelliHoleHeight, 0.0);
                    double the = Math.Max(m.MarinelliHoleEndWallThickness, 0.0);
                    double endWall = Math.Max(m.MarinelliEndWallThickness, 0.0);
                    double zSrc0 = zCeiling - the - Math.Max(hs - hh, 0.0);
                    left = -rOut;
                    right = rOut;
                    top = zSrc0 - endWall;
                    bottom = Math.Max(zSrc0 - endWall + Math.Max(m.MarinelliBeakerHeight, 0.0), zFace);
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
                    double endWall = Math.Max(m.MarinelliEndWallThickness, 0.0);
                    double hs = Math.Max(m.MarinelliSourceHeight, 0.0);
                    double hh = Math.Max(m.MarinelliHoleHeight, 0.0);
                    double zCeiling = zFace - Math.Max(m.MarinelliToDetectorDistance, 0.0);
                    double cap = Math.Max(0.0, hs - hh);
                    double zSrc0 = zCeiling - the - cap;
                    double body = Math.Max(m.MarinelliBeakerHeight, 0.0);

                    // Дно стакана — толщина ДОНЫШКА, а не борта: у стакана это
                    // разные поля, и рисовать дно бортом значило бы, что
                    // подсветка донышка показывает пустое место.
                    //
                    // Высота стакана — ПОЛНАЯ, снаружи: у RadiaCode 0.5 л это
                    // 8.9 при пробе 8.5 и донышке 0.2. Прежде тело рисовалось
                    // на `side` выше самого себя.
                    Fill(g, WallColor, -rOut, zSrc0 - endWall, 2.0 * rOut, body);
                    double rSrcOut = Math.Max(rh + ths, rOut - side);
                    Fill(g, SampleColor, -rSrcOut, zSrc0, 2.0 * rSrcOut, hs);
                    // колодец: вырез в пробе, стенка колодца и пустота внутри
                    Fill(g, WallColor, -(rh + ths), zCeiling - the, 2.0 * (rh + ths), the + hh);
                    Fill(g, Canvas, -rh, zCeiling, 2.0 * rh, hh);

                    using (Pen pen = new Pen(Ink, 1.2f))
                    {
                        Outline(g, pen, -rOut, zSrc0 - endWall, 2.0 * rOut, body);
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
                                  Math.Max(m.PointDistance, 0.0), "PointDistance");
                        return;

                    case GeometrySourceType.Cylinder:
                    {
                        double rOut = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                        double wall = Math.Max(m.BeakerSideWallThickness, 0.0);
                        double end = Math.Max(m.BeakerEndWallThickness, 0.0);
                        double hs = Math.Max(m.SourceHeight, 0.0);
                        double zWallTop = zFace - Math.Max(m.BeakerToDetectorDistance, 0.0);
                        double zSrcTop = zWallTop - end;
                        this.DimH(g, pen, ink, -rOut, rOut, this.AboveTop(22),
                                  2.0 * rOut, "BeakerDiameter");
                        this.DimV(g, pen, ink, this.RightOf(rOut, 26), zSrcTop - hs, zSrcTop, hs,
                                  "SourceHeight");
                        this.DimV(g, pen, ink, 0.0, zWallTop, zFace,
                                  Math.Max(m.BeakerToDetectorDistance, 0.0), "BeakerToDetectorDistance");
                        this.DimV(g, pen, ink, -rOut * 0.55, zSrcTop, zWallTop, end, "BeakerEndWallThickness");
                        this.DimH(g, pen, ink, -rOut, -(rOut - wall), zSrcTop - hs * 0.5, wall,
                                  "BeakerSideWallThickness");
                        this.DimV(g, pen, ink, rOut * 0.72, zSrcTop - hs, zWallTop,
                                  Math.Max(m.BeakerHeight, 0.0), "BeakerHeight");
                        return;
                    }

                    default:
                    {
                        double rOut = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                        double rh = 0.5 * Math.Max(m.MarinelliHoleDiameter, 0.0);
                        double ths = Math.Max(m.MarinelliHoleSideThickness, 0.0);
                        double side = Math.Max(m.MarinelliSideThickness, 0.0);
                        double hs = Math.Max(m.MarinelliSourceHeight, 0.0);
                        double hh = Math.Max(m.MarinelliHoleHeight, 0.0);
                        double the = Math.Max(m.MarinelliHoleEndWallThickness, 0.0);
                        double endWall = Math.Max(m.MarinelliEndWallThickness, 0.0);
                        double zCeiling = zFace - Math.Max(m.MarinelliToDetectorDistance, 0.0);
                        double cap = Math.Max(0.0, hs - hh);
                        double zSrc0 = zCeiling - the - cap;
                        double body = Math.Max(m.MarinelliBeakerHeight, 0.0);
                        this.DimH(g, pen, ink, -rOut, rOut, this.AboveTop(22),
                                  2.0 * rOut, "MarinelliBeakerDiameter");
                        this.DimH(g, pen, ink, -rh, rh, zCeiling + hh * 0.55, 2.0 * rh,
                                  "MarinelliHoleDiameter");
                        // 40 точек, а не 30: число DimV пишет СПРАВА от своей
                        // линии, то есть в зазор до стенки стакана. Тридцати
                        // хватало на «5», но не на «12.35» — оно налезало на
                        // тело. В поле (MarginX = 78) сорок помещается.
                        this.DimV(g, pen, ink, this.LeftOf(-rOut, 40), zSrc0, zSrc0 + hs, hs,
                                  "MarinelliSourceHeight");
                        this.DimV(g, pen, ink, rh * 0.55, zCeiling, zCeiling + hh, hh, "MarinelliHoleHeight");
                        // Корпус рисуется от zSrc0 - endWall высотой body
                        // (см. DrawSource), выноска обязана мерить ровно то же и
                        // показывать само поле, а не сумму с чем-нибудь.
                        this.DimV(g, pen, ink, this.RightOf(rOut, 26), zSrc0 - endWall,
                                  zSrc0 - endWall + body, body, "MarinelliBeakerHeight");
                        this.DimH(g, pen, ink, -rOut, -(rOut - side), zSrc0 + hs * 0.28, side,
                                  "MarinelliSideThickness");
                        // Донышко — своя толщина, а не боковая. Раньше здесь
                        // стояло side: поле подсвечивалось, а число показывало
                        // соседний размер.
                        this.DimV(g, pen, ink, -rOut * 0.72, zSrc0 - endWall, zSrc0, endWall,
                                  "MarinelliEndWallThickness");
                        // Стенки колодца разведены по высоте и по стороне: при
                        // общем 0.2 их подписи иначе налезают друг на друга и
                        // на выноску диаметра колодца.
                        this.DimH(g, pen, ink, rh, rh + ths, zCeiling + hh * 0.82, ths,
                                  "MarinelliHoleSideThickness");
                        this.DimV(g, pen, ink, -rh * 0.55, zCeiling - the, zCeiling, the,
                                  "MarinelliHoleEndWallThickness");
                        this.DimV(g, pen, ink, 0.0, zCeiling, zFace,
                                  Math.Max(m.MarinelliToDetectorDistance, 0.0),
                                  "MarinelliToDetectorDistance");
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

        /// <summary>
        /// Горизонтальный размер со стрелками и числом над линией. Подсвеченный
        /// рисуется красным и жирнее — вместе с линией и стрелками, а не только
        /// числом: у тонкого слоя число стоит вплотную к соседнему, и одного
        /// цвета цифры мало, чтобы понять, к чему она относится.
        /// </summary>
        void DimH(Graphics g, Pen pen, Brush ink, double x1, double x2, double z, double value, string key)
        {
            float a = this.X(x1), b = this.X(x2), y = this.Y(z);
            if (Math.Abs(b - a) < 2f)
            {
                return;
            }

            bool lit = this.Lit(key);
            using (Pen litPen = lit ? new Pen(LitColor, 1.8f) : null)
            using (Brush litInk = lit ? new SolidBrush(LitColor) : null)
            {
                Pen p = lit ? litPen : pen;
                Brush b2 = lit ? litInk : ink;
                g.DrawLine(p, a, y, b, y);
                Arrow(g, p, a, y, +1f, 0f);
                Arrow(g, p, b, y, -1f, 0f);
                string text = Format(value);
                SizeF size = g.MeasureString(text, this.Font);
                g.DrawString(text, this.Font, b2, (a + b) / 2f - size.Width / 2f, y - size.Height - 1f);
            }
        }

        /// <summary>Вертикальный размер со стрелками и числом справа.</summary>
        void DimV(Graphics g, Pen pen, Brush ink, double x, double z1, double z2, double value, string key)
        {
            float a = this.Y(z1), b = this.Y(z2), xx = this.X(x);
            if (Math.Abs(b - a) < 2f)
            {
                return;
            }

            bool lit = this.Lit(key);
            using (Pen litPen = lit ? new Pen(LitColor, 1.8f) : null)
            using (Brush litInk = lit ? new SolidBrush(LitColor) : null)
            {
                Pen p = lit ? litPen : pen;
                Brush b2 = lit ? litInk : ink;
                g.DrawLine(p, xx, a, xx, b);
                Arrow(g, p, xx, a, 0f, +1f);
                Arrow(g, p, xx, b, 0f, -1f);
                string text = Format(value);
                SizeF size = g.MeasureString(text, this.Font);
                g.DrawString(text, this.Font, b2, xx + 3f, (a + b) / 2f - size.Height / 2f);
            }
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
