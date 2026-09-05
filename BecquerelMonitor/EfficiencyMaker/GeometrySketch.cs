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
        // Мерка чертежа
        // ------------------------------------------------------------------

        /// <summary>Прямоугольник напечатанной подписи вместе с её текстом и ключом поля.</summary>
        public struct SketchLabel
        {
            public RectangleF Bounds;
            public string Text;
            public string Key;
        }

        readonly List<SketchLabel> labels = new List<SketchLabel>();

        /// <summary>
        /// Прямоугольники ВСЕХ подписей последней отрисовки.
        ///
        /// Приёмка у чертежа одна и она числовая: ни одна подпись не налезла на
        /// соседнюю и ни одна не ушла за поле. Глазами это проверялось до
        /// 16.08.2026 и пропустило `E28` — на сцене «в лунке» «51.9» и «547.4»
        /// печатались в одной точке. Список наполняется в самой отрисовке, тем
        /// же размером, каким текст и напечатан, поэтому мерит именно то, что
        /// увидит человек, а не пересчёт по числу знаков.
        /// </summary>
        public SketchLabel[] Labels
        {
            get { return this.labels.ToArray(); }
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
            this.labels.Clear();

            using (Pen frame = new Pen(Color.FromArgb(0x90, 0x90, 0x90)))
            {
                g.DrawRectangle(frame, 0, 0, this.Width - 1, this.Height - 1);
            }

            GeometryModel m = this.model;
            if (m == null)
            {
                return;
            }

            // Размеры детектора. У бруска в разрезе видна грань, ОБРАЩЁННАЯ К
            // ПРОБЕ; третий размер уходит в глубину и подписывается отдельно,
            // иначе разрез врал бы о форме. При боковой постановке (E21) брусок
            // развёрнут, и чертёж обязан показывать именно развёрнутый: иначе
            // человек увидит одно, а посчитается другое.
            double halfWidth, boxDepthIntoPage, height;
            if (m.Shape == CrystalShape.Box)
            {
                double hx, hy, d;
                m.CrystalBoxInScene(out hx, out hy, out d);
                halfWidth = Math.Max(hx, 0.0);
                boxDepthIntoPage = Math.Max(2.0 * hy, 0.0);
                height = Math.Max(d, 0.0);
            }
            else
            {
                halfWidth = 0.5 * Math.Max(m.CrystalDiameter, 0.0);
                boxDepthIntoPage = 0.0;
                height = Math.Max(m.CrystalHeight, 0.0);
            }

            double tfr = Math.Max(m.FrontReflectorThickness, 0.0);
            double tsr = Math.Max(m.SideReflectorThickness, 0.0);
            double tfc = Math.Max(m.FrontCladdingThickness, 0.0);
            double tsc = Math.Max(m.SideCladdingThickness, 0.0);
            // Та же перестановка, что в симуляторе: к пробе обращена обвязка
            // ТОЙ стороны, у которой она стоит.
            if (m.Facing == GeometryDetectorFacing.Side)
            {
                double t = tfr; tfr = tsr; tsr = t;
                t = tfc; tfc = tsc; tsc = t;
            }
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
                this.Annotate(g, m, halfWidth, height, boxDepthIntoPage, tfr, tsr, tfc, tsc, tm);
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
            // Первая строка — ДЕТЕКТОР, вторая — образец, и называются они
            // по-разному нарочно: раньше обе начинались с формы («Box», «Box»),
            // и по подписи нельзя было понять, где кристалл, а где проба.
            lines.Add(m.Shape == CrystalShape.Box
                ? string.Format(CultureInfo.InvariantCulture, "{0}: {1:G4} x {2:G4} x {3:G4} mm",
                                Resources.EfficiencySketchDetector,
                                m.CrystalBoxX, m.CrystalBoxY, m.CrystalBoxZ)
                : string.Format(CultureInfo.InvariantCulture, "{0}: {1}{2:G4} x {3:G4} mm",
                                Resources.EfficiencySketchDetector, "⌀",
                                m.CrystalDiameter, m.CrystalHeight));

            // E21: развёрнутый брусок на разрезе выглядит как другой кристалл —
            // размеры в первой строке те же, а пропорции иные. Без этой строки
            // чертёж читался бы как ошибка ввода.
            if (m.Facing == GeometryDetectorFacing.Side)
            {
                lines.Add(Resources.EfficiencySketchFacingSide);
            }

            switch (m.SourceType)
            {
                case GeometrySourceType.Point:
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:G4} mm",
                                            Resources.EfficiencySketchPoint,
                                            Math.Max(m.PointDistance, 0.0)));
                    break;

                case GeometrySourceType.Box:
                    lines.Add(string.Format(CultureInfo.InvariantCulture,
                                            "{0}: {1:G4} x {2:G4} x {3:G4} mm",
                                            Resources.EfficiencySketchBox,
                                            Math.Max(m.BoxSourceX, 0.0),
                                            Math.Max(m.BoxSourceY, 0.0),
                                            Math.Max(m.BoxSourceHeight, 0.0)));
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:G4} mm",
                                            Resources.EfficiencySketchDistance,
                                            Math.Max(m.BoxToDetectorDistance, 0.0)));
                    break;

                case GeometrySourceType.Cylinder:
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1}{2:G4} x {3:G4} mm",
                                            Resources.EfficiencySketchCylinder, "⌀",
                                            Math.Max(m.BeakerDiameter, 0.0),
                                            Math.Max(m.SourceHeight, 0.0)));
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:G4} mm",
                                            Resources.EfficiencySketchDistance,
                                            Math.Max(m.BeakerToDetectorDistance, 0.0)));
                    break;

                default:
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1}{2:G4} x {3:G4} mm",
                                            Resources.EfficiencySketchMarinelli, "⌀",
                                            Math.Max(m.MarinelliBeakerDiameter, 0.0),
                                            Math.Max(m.MarinelliBeakerHeight, 0.0)));
                    break;
            }

            // Табличка обязана поместиться в отведённое ей поле, а поле у
            // миниатюры чужое: её ширину задаёт окно конфигурации прибора.
            // Строка «Точечный источник, дистанция: 100 mm» на 220 точках
            // вылезала за правый край НЕЗАМЕТНО — обрезанный текст выглядит как
            // короткий (мерено 05.09.2026 приёмкой `EditorShot --check`).
            // Поэтому шрифт таблички ужимается ровно настолько, чтобы влезть, и
            // ни на волос больше.
            Font font = this.Font;
            float width, lineHeight;
            Measure(g, lines, font, out width, out lineHeight);
            float roomX = this.Width - 12f, roomY = this.Height - 10f;
            float shrink = Math.Min(width > roomX ? roomX / width : 1f,
                                    lineHeight * lines.Count > roomY
                                        ? roomY / (lineHeight * lines.Count) : 1f);
            Font small = null;
            if (shrink < 1f)
            {
                small = new Font(font.FontFamily, Math.Max(font.Size * shrink, 5f),
                                 font.Style, font.Unit);
                font = small;
                Measure(g, lines, font, out width, out lineHeight);
            }

            using (small)
            using (Brush plate = new SolidBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF)))
            using (Brush ink = new SolidBrush(Ink))
            {
                g.FillRectangle(plate, 4f, 4f, width + 8f, lineHeight * lines.Count + 6f);
                for (int i = 0; i < lines.Count; i++)
                {
                    this.Print(g, font, ink, lines[i], null, 8f, 6f + i * lineHeight,
                               g.MeasureString(lines[i], font));
                }
            }
        }

        static void Measure(Graphics g, List<string> lines, Font font,
                            out float width, out float lineHeight)
        {
            width = 0f;
            lineHeight = 0f;
            foreach (string line in lines)
            {
                SizeF size = g.MeasureString(line, font);
                width = Math.Max(width, size.Width);
                lineHeight = Math.Max(lineHeight, size.Height);
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
                      double boxDepthIntoPage,
                      double tfr, double tsr, double tfc, double tsc, double tm)
        {
            double outerHalf = halfWidth + tsr + tsc;
            double zFace = -(tfr + tfc);

            bool box = m.Shape == CrystalShape.Box;
            // Подписи идут за РАЗВОРОТОМ: после боковой постановки высота на
            // чертеже взята уже не из Z, и подпись «CrystalBoxZ» рядом с ней
            // была бы ложью. Какое поле куда попало, решает сама модель —
            // в одном месте, чтобы чертёж и счёт не разошлись.
            string widthKey = "CrystalDiameter", lengthKey = "CrystalHeight", pageKey = null;
            if (box)
            {
                double hx, hy, d;
                m.CrystalBoxInScene(out hx, out hy, out d, out widthKey, out pageKey, out lengthKey);
            }

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
                    // Рядом с телом, а не в углу: в углу лежит табличка с
                    // размерами и накрывает надпись собой.
                    this.Note(g, ink, boxDepthIntoPage, 0.0, height + tm, 18f, pageKey);
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

                case GeometrySourceType.Box:
                {
                    // Разрез идёт по оси X, поэтому в кадр берётся сторона X.
                    double half = 0.5 * Math.Max(m.BoxSourceX, 0.0);
                    double zTop = zFace - Math.Max(m.BoxToDetectorDistance, 0.0);
                    left = -half;
                    right = half;
                    top = zTop - Math.Max(m.BoxEndWallThickness, 0.0)
                          - Math.Max(m.BoxSourceHeight, 0.0);
                    bottom = zFace;
                    return;
                }

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

                case GeometrySourceType.Box:
                {
                    double half = 0.5 * Math.Max(m.BoxSourceX, 0.0);
                    double wallB = Math.Max(m.BoxSideWallThickness, 0.0);
                    double endB = Math.Max(m.BoxEndWallThickness, 0.0);
                    double hsB = Math.Max(m.BoxSourceHeight, 0.0);
                    double zWallTopB = zFace - Math.Max(m.BoxToDetectorDistance, 0.0);
                    double zSrcTopB = zWallTopB - endB;
                    Fill(g, WallColor, -half, zSrcTopB - hsB, 2.0 * half, hsB + endB);
                    Fill(g, SampleColor, -(half - wallB), zSrcTopB - hsB,
                         2.0 * (half - wallB), hsB);
                    using (Pen pen = new Pen(Ink, 1.2f))
                    {
                        Outline(g, pen, -half, zSrcTopB - hsB, 2.0 * half, hsB + endB);
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

                    case GeometrySourceType.Box:
                    {
                        double half = 0.5 * Math.Max(m.BoxSourceX, 0.0);
                        double endB = Math.Max(m.BoxEndWallThickness, 0.0);
                        double hsB = Math.Max(m.BoxSourceHeight, 0.0);
                        double zWallTopB = zFace - Math.Max(m.BoxToDetectorDistance, 0.0);
                        double zSrcTopB = zWallTopB - endB;
                        this.DimH(g, pen, ink, -half, half, this.AboveTop(22),
                                  2.0 * half, "BoxSourceX");
                        this.DimV(g, pen, ink, this.RightOf(half, 26), zSrcTopB - hsB, zSrcTopB,
                                  hsB, "BoxSourceHeight");
                        this.DimV(g, pen, ink, 0.0, zWallTopB, zFace,
                                  Math.Max(m.BoxToDetectorDistance, 0.0), "BoxToDetectorDistance");
                        this.DimV(g, pen, ink, -half * 0.55, zSrcTopB, zWallTopB, endB,
                                  "BoxEndWallThickness");
                        double wallB = Math.Max(m.BoxSideWallThickness, 0.0);
                        this.DimH(g, pen, ink, -half, -(half - wallB), zSrcTopB - hsB * 0.5,
                                  wallB, "BoxSideWallThickness");
                        // Слева от тела, на его середине: сверху стоит размер
                        // стороны X, снизу по оси идёт выноска расстояния, а
                        // внутри у левого края — толщина стенки.
                        this.Note(g, ink, m.BoxSourceY, -half, zSrcTopB - hsB * 0.5,
                                  -6f, "BoxSourceY", -8f);
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
                        this.DimH(g, pen, ink, -rOut, rOut, this.AboveTop(22),
                                  2.0 * rOut, "BeakerDiameter");
                        this.DimV(g, pen, ink, this.RightOf(rOut, 26), zSrcTop - hs, zSrcTop, hs,
                                  "SourceHeight");
                        this.DimV(g, pen, ink, 0.0, zWallTop, zFace,
                                  Math.Max(m.BeakerToDetectorDistance, 0.0), "BeakerToDetectorDistance");
                        this.DimV(g, pen, ink, -rOut * 0.55, zSrcTop, zWallTop, end, "BeakerEndWallThickness");
                        this.DimH(g, pen, ink, -rOut, -(rOut - wall), zSrcTop - hs * 0.5,
                                  wall, "BeakerSideWallThickness");
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
        /// Горизонтальный размер: размерная линия, окончания и число.
        /// Подсвеченный рисуется красным и жирнее — вместе с линией и стрелками,
        /// а не только числом: у тонкого слоя число стоит вплотную к соседнему,
        /// и одного цвета цифры мало, чтобы понять, к чему она относится.
        ///
        /// Размера, которого НЕТ (ноль), на чертеже нет — нулевой слой не
        /// подписывается. А вот размер, который есть, но узок на экране,
        /// подписывается ОБЯЗАТЕЛЬНО: прежде он молча пропадал по правилу
        /// «короче двух точек — не рисуем», и на сцене «в лунке» колодец Ø51.9
        /// при габарите 1512 исчезал с чертежа целиком (`E28`).
        /// </summary>
        void DimH(Graphics g, Pen pen, Brush ink, double x1, double x2, double z, double value, string key)
        {
            if (!(value > 0.0))
            {
                return;
            }

            float a = this.X(x1), b = this.X(x2), y = this.Y(z);
            if (a > b)
            {
                float t = a;
                a = b;
                b = t;
            }

            bool lit = this.Lit(key);
            using (Pen litPen = lit ? new Pen(LitColor, 1.8f) : null)
            using (Brush litInk = lit ? new SolidBrush(LitColor) : null)
            {
                Pen p = lit ? litPen : pen;
                Brush b2 = lit ? litInk : ink;
                Ends(g, p, true, a, b, y);
                string text = Format(value);
                SizeF size = g.MeasureString(text, this.Font);
                this.Place(g, p, b2, text, key, SpotsH(a, b, y, size));
            }
        }

        /// <summary>Вертикальный размер: то же самое, повёрнутое на четверть.</summary>
        void DimV(Graphics g, Pen pen, Brush ink, double x, double z1, double z2, double value, string key)
        {
            if (!(value > 0.0))
            {
                return;
            }

            float a = this.Y(z1), b = this.Y(z2), xx = this.X(x);
            if (a > b)
            {
                float t = a;
                a = b;
                b = t;
            }

            bool lit = this.Lit(key);
            using (Pen litPen = lit ? new Pen(LitColor, 1.8f) : null)
            using (Brush litInk = lit ? new SolidBrush(LitColor) : null)
            {
                Pen p = lit ? litPen : pen;
                Brush b2 = lit ? litInk : ink;
                Ends(g, p, false, a, b, xx);
                string text = Format(value);
                SizeF size = g.MeasureString(text, this.Font);
                this.Place(g, p, b2, text, key, SpotsV(xx, a, b, size));
            }
        }

        /// <summary>
        /// Напечатать подпись и ЗАПОМНИТЬ её прямоугольник. Один вход на все
        /// подписи чертежа: подпись, напечатанная мимо этого метода, для
        /// приёмки не существует.
        /// </summary>
        void Print(Graphics g, Brush ink, string text, string key, float x, float y, SizeF size)
        {
            this.Print(g, this.Font, ink, text, key, x, y, size);
        }

        void Print(Graphics g, Font font, Brush ink, string text, string key,
                   float x, float y, SizeF size)
        {
            g.DrawString(text, font, ink, x, y);
            this.labels.Add(new SketchLabel
            {
                Bounds = new RectangleF(x, y, size.Width, size.Height),
                Text = text,
                Key = key,
            });
        }

        /// <summary>
        /// Надпись «Y = ... mm» о размере, которого в разрезе не видно. Ставится
        /// рядом с телом, к которому относится, и подсвечивается по тому же
        /// ключу, что и обычный размер, — иначе поле в редакторе подсвечивать
        /// нечем.
        /// </summary>
        void Note(Graphics g, Brush ink, double value, double x, double z,
                  float dy, string key, float dx = 0f)
        {
            string text = string.Format(CultureInfo.InvariantCulture, "Y = {0:G4} mm", value);
            SizeF size = g.MeasureString(text, this.Font);
            // dx = 0 — по центру над точкой, отрицательное — надпись КОНЧАЕТСЯ
            // левее её, положительное — начинается правее. Так подпись уводится
            // от тела и от чужих выносок, не подбирая координату на глаз.
            float px = dx < 0f ? this.X(x) + dx - size.Width
                     : dx > 0f ? this.X(x) + dx
                     : this.X(x) - size.Width / 2f;
            float py = this.Y(z) + (dy >= 0f ? dy : dy - size.Height);
            // Своё место — первое, но не единственное: если его заняли, надпись
            // уходит на полку выноски от того же тела, а не печатается поверх
            // соседа.
            List<Spot> spots = new List<Spot> { At(px, py, size) };
            Shelves(spots, new PointF(this.X(x), this.Y(z)), false, size);

            bool lit = this.Lit(key);
            using (Pen leader = new Pen(lit ? LitColor : Ink, 1f))
            using (Brush litInk = lit ? new SolidBrush(LitColor) : null)
            {
                this.Place(g, leader, lit ? litInk : ink, text, key, spots);
            }
        }

        // ------------------------------------------------------------------
        // Раскладка размерных чисел по ГОСТ 2.307-2011
        //
        // Правило одно и оно старше нас: ЕСЛИ МЕСТА НЕ ХВАТАЕТ, размерное число
        // и стрелки ВЫНОСЯТ за пределы выносных линий — сначала на продолжение
        // размерной линии, а если и там занято, то на полку линии-выноски.
        // Масштаб при этом остаётся линейным: логарифмический прочёл бы чертёж,
        // но соврал бы про пропорции, а чертёж затем и нужен, чтобы ошибка в
        // размере ВЫГЛЯДЕЛА ошибкой.
        //
        // Место меряется НЕ числом знаков, а `MeasureString` — тем же, чем
        // текст потом и печатается. Ширина «547.4» и «12» отличается вдвое, и
        // порог, посчитанный по знакам, врал бы ровно там, где решается дело.
        // ------------------------------------------------------------------

        /// <summary>Зазор, который подпись требует вокруг себя, точек.</summary>
        const float Gap = 2f;

        /// <summary>Отступ числа от конца размерной линии, точек.</summary>
        const float Reach = 5f;

        /// <summary>Вылет размерной линии за выносную, когда стрелки снаружи.</summary>
        const float Tail = 9f;

        /// <summary>Длина наклонной части линии-выноски, точек.</summary>
        const float ElbowRun = 12f;

        /// <summary>Короче этого стрелки внутрь не помещаются.</summary>
        const float ArrowRoom = 12f;

        /// <summary>Насколько отводятся полки выносок, точек.</summary>
        static readonly float[] ShelfSteps = { 16f, 28f, 40f, 54f, 70f, 88f, 108f, 130f };

        /// <summary>Место, куда можно поставить число, и выноска к нему.</summary>
        struct Spot
        {
            public RectangleF Box;
            public PointF[] Leader;
        }

        static Spot At(float x, float y, SizeF size)
        {
            return new Spot { Box = new RectangleF(x, y, size.Width, size.Height) };
        }

        /// <summary>
        /// Полка линии-выноски: от точки на теле идёт наклонная, переходящая в
        /// горизонтальную полку, число — НАД полкой.
        /// </summary>
        static Spot Shelf(PointF anchor, float dx, float dy, float side, SizeF size)
        {
            PointF elbow = new PointF(anchor.X + dx, anchor.Y + dy);
            PointF end = new PointF(elbow.X + side * (size.Width + 6f), elbow.Y);
            float tx = side > 0f ? elbow.X + 3f : elbow.X - size.Width - 3f;
            return new Spot
            {
                Box = new RectangleF(tx, elbow.Y - size.Height, size.Width, size.Height),
                Leader = new PointF[] { anchor, elbow, end },
            };
        }

        /// <summary>
        /// Полки во все четыре стороны, с растущим отводом. Стороны чередуются
        /// внутри одного отвода: цепочка мелких размеров подряд так сама собой
        /// раскладывается веером, а не столбиком в одну сторону.
        /// </summary>
        static void Shelves(List<Spot> list, PointF anchor, bool horizontalDim, SizeF size)
        {
            foreach (float off in ShelfSteps)
            {
                // У горизонтального размера выноска поднимается/опускается, у
                // вертикального — отходит вбок: иначе она легла бы вдоль своей
                // же размерной линии и стала невидимой.
                float ax = horizontalDim ? ElbowRun : off;
                float ay = horizontalDim ? off : ElbowRun;
                list.Add(Shelf(anchor, +ax, -ay, +1f, size));
                list.Add(Shelf(anchor, -ax, -ay, -1f, size));
                list.Add(Shelf(anchor, +ax, +ay, +1f, size));
                list.Add(Shelf(anchor, -ax, +ay, -1f, size));
            }
        }

        /// <summary>Места для числа горизонтального размера, по убыванию желанности.</summary>
        static List<Spot> SpotsH(float a, float b, float y, SizeF size)
        {
            float mid = (a + b) / 2f;
            List<Spot> list = new List<Spot>();
            if (size.Width + 6f <= b - a)
            {
                // Своё место — над серединой размерной линии.
                list.Add(At(mid - size.Width / 2f, y - size.Height - 1f, size));
                // Вторая половина «чередования» цепочки: соседнее число ниже.
                list.Add(At(mid - size.Width / 2f, y + 2f, size));
            }

            // На ПРОДОЛЖЕНИИ размерной линии — сперва вправо, как принято.
            list.Add(At(b + Reach, y - size.Height - 1f, size));
            list.Add(At(a - Reach - size.Width, y - size.Height - 1f, size));
            list.Add(At(b + Reach, y + 2f, size));
            list.Add(At(a - Reach - size.Width, y + 2f, size));

            Shelves(list, new PointF(mid, y), true, size);
            return list;
        }

        /// <summary>Места для числа вертикального размера.</summary>
        static List<Spot> SpotsV(float x, float a, float b, SizeF size)
        {
            float mid = (a + b) / 2f;
            List<Spot> list = new List<Spot>();
            if (size.Height + 4f <= b - a)
            {
                list.Add(At(x + 3f, mid - size.Height / 2f, size));
                list.Add(At(x - 3f - size.Width, mid - size.Height / 2f, size));
            }

            list.Add(At(x + 3f, a - size.Height - Reach, size));
            list.Add(At(x + 3f, b + Reach, size));
            list.Add(At(x - 3f - size.Width, a - size.Height - Reach, size));
            list.Add(At(x - 3f - size.Width, b + Reach, size));

            Shelves(list, new PointF(x, mid), false, size);
            return list;
        }

        /// <summary>
        /// Поставить число в ПЕРВОЕ место, где оно и в поле помещается, и ни на
        /// кого не налезает.
        ///
        /// Если свободного нет ни одного, число всё равно печатается — в
        /// наименее занятом: потерянное число хуже прижатого, и приёмка такой
        /// случай увидит числом, а не пропустит молча.
        /// </summary>
        void Place(Graphics g, Pen pen, Brush ink, string text, string key, List<Spot> spots)
        {
            int best = 0;
            float bestCost = float.MaxValue;
            for (int i = 0; i < spots.Count; i++)
            {
                float cost = this.Cost(spots[i].Box);
                if (cost <= 0f)
                {
                    best = i;
                    break;
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }

            Spot spot = spots[best];
            if (spot.Leader != null)
            {
                g.DrawLines(pen, spot.Leader);
            }

            this.Print(g, ink, text, key, spot.Box.X, spot.Box.Y,
                       new SizeF(spot.Box.Width, spot.Box.Height));
        }

        /// <summary>
        /// Чем плохо место: площадь перекрытия с уже поставленными подписями
        /// плюс большой штраф за выход за поле. Ноль — место годно.
        /// </summary>
        float Cost(RectangleF box)
        {
            RectangleF field = new RectangleF(1f, 1f, this.Width - 2f, this.Height - 2f);
            float cost = 0f;
            if (!field.Contains(box))
            {
                cost = 1e6f + Math.Max(0f, field.X - box.X) + Math.Max(0f, field.Y - box.Y)
                     + Math.Max(0f, box.Right - field.Right) + Math.Max(0f, box.Bottom - field.Bottom);
            }

            RectangleF grown = new RectangleF(box.X - Gap, box.Y - Gap,
                                              box.Width + 2f * Gap, box.Height + 2f * Gap);
            foreach (SketchLabel other in this.labels)
            {
                RectangleF hit = RectangleF.Intersect(grown, other.Bounds);
                if (hit.Width > 0f && hit.Height > 0f)
                {
                    cost += hit.Width * hit.Height + 1f;
                }
            }

            return cost;
        }

        /// <summary>
        /// Размерная линия с окончаниями. По ГОСТ 2.307-2011: широкий размер —
        /// стрелки внутри; узкий — стрелки ВЫНОСЯТСЯ наружу, остриями к выносным
        /// линиям; совсем узкий — вместо стрелок засечки, иначе две стрелки
        /// сливаются в кляксу.
        ///
        /// Сама деталь при этом НЕ раздувается — колодец в один пиксель так
        /// пикселем и остаётся, честно. Видимой обязана быть ВЫНОСКА: линия
        /// выходит за деталь на <see cref="Tail"/> точек, и от неё уже тянется
        /// полка с числом.
        /// </summary>
        static void Ends(Graphics g, Pen pen, bool horizontal, float a, float b, float fixedCoord)
        {
            float span = b - a;
            float tail = span >= ArrowRoom ? 0f : Tail;
            if (horizontal)
            {
                g.DrawLine(pen, a - tail, fixedCoord, b + tail, fixedCoord);
            }
            else
            {
                g.DrawLine(pen, fixedCoord, a - tail, fixedCoord, b + tail);
            }

            if (span >= ArrowRoom)
            {
                Arrow(g, pen, horizontal, a, fixedCoord, +1f);
                Arrow(g, pen, horizontal, b, fixedCoord, -1f);
            }
            else if (span >= 3f)
            {
                Arrow(g, pen, horizontal, a, fixedCoord, -1f);
                Arrow(g, pen, horizontal, b, fixedCoord, +1f);
            }
            else
            {
                Tick(g, pen, horizontal, a, fixedCoord);
                Tick(g, pen, horizontal, b, fixedCoord);
            }
        }

        /// <summary>Стрелка остриём в точку, оперением в сторону <paramref name="dir"/>.</summary>
        static void Arrow(Graphics g, Pen pen, bool horizontal, float at, float fixedCoord, float dir)
        {
            const float S = 4f;
            if (horizontal)
            {
                g.DrawLine(pen, at, fixedCoord, at + dir * S, fixedCoord - S * 0.6f);
                g.DrawLine(pen, at, fixedCoord, at + dir * S, fixedCoord + S * 0.6f);
            }
            else
            {
                g.DrawLine(pen, fixedCoord, at, fixedCoord - S * 0.6f, at + dir * S);
                g.DrawLine(pen, fixedCoord, at, fixedCoord + S * 0.6f, at + dir * S);
            }
        }

        /// <summary>Засечка вместо стрелки — короткий наклонный штрих (ГОСТ 2.307).</summary>
        static void Tick(Graphics g, Pen pen, bool horizontal, float at, float fixedCoord)
        {
            const float S = 3f;
            if (horizontal)
            {
                g.DrawLine(pen, at - S * 0.5f, fixedCoord + S, at + S * 0.5f, fixedCoord - S);
            }
            else
            {
                g.DrawLine(pen, fixedCoord + S, at - S * 0.5f, fixedCoord - S, at + S * 0.5f);
            }
        }

        /// <summary>
        /// Число на выноске, миллиметры. Имя не Text: так называется свойство
        /// Control. Четыре значащих на всех: в миллиметрах обычные размеры —
        /// десятки и сотни, и прежних трёх на 114.5 или 18.54 не хватало.
        /// </summary>
        static string Format(double value)
        {
            return value.ToString("G4", CultureInfo.InvariantCulture);
        }
    }
}
