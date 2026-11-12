using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Чертёж импортированной геометрии GDML: слева разрез плоскостью через ось
    /// прибора, справа поперечное сечение (вид с торца). Решение Amber
    /// 29.10.2026 вопросником, дословно: «Разрез + вид с торца».
    ///
    /// Тела вращения (polycone, genericPolycone) рисуются заливкой профиля,
    /// параллелепипеды — сечением коробки, сетки tessellated — линиями
    /// пересечения треугольников с плоскостью. Цвет — по веществу; вычитание
    /// (subtraction) вырезает вычтенное: сквозь вырез видно то, что лежит ниже.
    ///
    /// Оформление (`AMBER301`, дизайн 05.11.2026): тот же язык, что у чертежа редактора
    /// <see cref="GeometrySketch"/> — холст, цвета кристалла, отражателя, корпуса, пробы и стенки,
    /// шрифт, знак ⌀ и размеры со стрелками, два вида одним масштабом. Всё, что не геометрия,
    /// стоит ВНЕ фигур: заголовок и подпись-отказ над видом, выноски в полях, легенда веществ снизу.
    ///
    /// ⛔ (`AMBER292`, `AMBER304`) Чертёж либо верен, либо говорит подписью, чего на нём
    /// нет: тело, сечение которого здесь не построить (наклонная плоскость, неполный
    /// оборот на хорде), не подменяется другой фигурой. Неконечные границы или масштаб —
    /// текст вместо чертежа, а не исключение из OnPaint.
    /// </summary>
    public sealed class GdmlSketch : Control
    {
        static readonly Color Canvas = Color.FromArgb(0xD6, 0xD2, 0xC4);
        static readonly Color Ink = Color.FromArgb(0x20, 0x20, 0x20);
        static readonly Color Divider = Color.FromArgb(0x90, 0x90, 0x90);

        /// <summary>Пояснения: вторая ступень важности, на холсте читается (контраст 6:1).</summary>
        static readonly Color Dim = Color.FromArgb(0x4C, 0x4A, 0x44);

        /// <summary>Полоска у подписи о том, чего на чертеже нет.</summary>
        static readonly Color NoteInk = Color.FromArgb(0xB0, 0x5A, 0x10);

        /// <summary>Ось и засечки размеров: тонко и полупрозрачно, поверх тел не спорят с контурами.</summary>
        static readonly Color GuideInk = Color.FromArgb(0xA0, 0x20, 0x20, 0x20);

        /// <summary>Поле рамки вида, точек.</summary>
        const double FrameMargin = 16.0;

        /// <summary>
        /// Допуск на угол между осью тела и плоскостью, рад. Обычная запись поворота без единицы —
        /// `1.5707963` (отклонение 2.7e-8) или `1.5708` (3.7e-6); на метре 1e-5 рад — 0.01 мм,
        /// на чертеже этого нет. `1.57` (8e-4) — уже косо.
        /// </summary>
        const double AxisTolerance = 1e-5;

        /// <summary>Сколько точек ребра составного тела проверяется на вид: шаг растёт, а не время.</summary>
        const double MaxContourSamples = 20000.0;

        /// <summary>Глубина вложенности вычитаний на чертеже: тот же предел, что у читателя (`AMBER292`).</summary>
        const int MaxShapeDepth = GdmlGeometry.MaxSolidDepth;

        /// <summary>
        /// Частей одного тела при развёртке в дерево. L(i) = L(i−1) − L(i−1) в полтора килобайта файла
        /// разворачивается в 2^i частей; настоящие тела набора — единицы частей. Замер 05.11.2026:
        /// цепочка в 511 частей рисуется 1.2 с, клип растёт квадратично, в 299 частей — 0.2 с (проба, вход chainF_149).
        /// </summary>
        const long MaxShapeParts = 300;

        /// <summary>Общая работа на один вид по всем вычитаниям, частей: перерисовка идёт в потоке окна.</summary>
        const long MaxViewParts = 600;

        /// <summary>
        /// Работа вычитания: вершины сечения всего дерева на глубину вложенности сверх одного уровня.
        /// Каждая вершина проходит через клип каждого уровня, поэтому считать тела мало: пять килобайт
        /// полиаконов в 50 плоскостей занимали 1.3 с, сто уровней — 62 с. Одно вычитание стоит 0: оно
        /// линейно по вершинам, как обычное тело и как прежний чертёж.
        ///
        /// Замер 05.11.2026 на 1400×700: 2.4–6 мкс на единицу, то есть 100000 единиц — до 0.6 с на
        /// отрисовку. Настоящие тела набора: RC103 и RC110 — десятки единиц, в GS2020 вычитаний нет.
        /// Предел на вид, по всем вычитаниям разом.
        /// </summary>
        const long MaxViewWork = 100000;

        /// <summary>
        /// Предел на сумму вершин всех вычитаний вида, включая одно вычитание, которому работа считает
        /// ноль. Вырез клипом квадратичен в GDI+: два тела по 3000 плоскостей занимали 14 с, 5000 колец
        /// одного тела — 25 с, и всё это в потоке окна. Замер 05.11.2026: при такой сумме 0.2–1.7 с на
        /// отрисовку. Цена: вычитание тела больше 1250 плоскостей — подпись. Настоящие файлы — десятки вершин.
        /// </summary>
        const long MaxViewVertices = 10000;

        GdmlGeometry geometry;

        /// <summary>Кто есть кто: роли томов, цвета и массы веществ. Считается один раз на геометрию.</summary>
        GdmlOverview overview;

        /// <summary>Шрифт заголовков: жирный от шрифта окна, как у чертежа редактора.</summary>
        Font labelFont;
        Font labelFontOf;

        /// <summary>Общий масштаб двух видов, точек на мм; 0 — у каждого вида свой (последняя раскладка).</summary>
        double commonScale;

        /// <summary>Сколько точек заняла подпись-отказ каждого вида в прошлой отрисовке: 0 — разрез, 1 — торец.</summary>
        readonly float[] reservedNotes = new float[2];

        /// <summary>Сколько заняла в этой.</summary>
        readonly float[] neededNotes = new float[2];

        /// <summary>Места видов последней отрисовки, по порядку: разрез, торец. Читает проба.</summary>
        readonly List<ViewSlot> paintSlots = new List<ViewSlot>();

        /// <summary>Все подписи последней отрисовки с их прямоугольниками: приёмка наложений числом, как у чертежа редактора.</summary>
        readonly List<SketchMark> paintMarks = new List<SketchMark>();

        /// <summary>Выноски последней отрисовки: куда показывают. Читает проба.</summary>
        readonly List<SketchCallout> paintCallouts = new List<SketchCallout>();

        /// <summary>Метки масштаба последней отрисовки: прямоугольник отрезка и длина в мм. Читает проба.</summary>
        readonly List<KeyValuePair<RectangleF, double>> paintScales = new List<KeyValuePair<RectangleF, double>>();

        /// <summary>Сведение вершин сетки по координатам: считается один раз на тело.</summary>
        readonly Dictionary<GdmlSolid, int[]> meshIds = new Dictionary<GdmlSolid, int[]>();

        /// <summary>Подпись и её прямоугольник.</summary>
        public struct SketchMark
        {
            public RectangleF Bounds;
            public string Text;
        }

        /// <summary>Выноска: роль, точка в теле и прямоугольник подписи.</summary>
        public struct SketchCallout
        {
            public GdmlRole Role;
            public string MaterialName;
            public bool Axial;
            public PointF Anchor;
            public PointF From;
            public RectangleF Label;
        }

        /// <summary>Почему последняя рамка не построена; null — просто нет места, и текст не нужен.</summary>
        string frameProblem;

        /// <summary>
        /// Тела текущего вида, которых нет на чертеже: тело не поддержано (<see cref="GdmlGeometry.IsUnsupported"/>).
        /// (`AMBER301`, fixGdml2) Отдельно от <see cref="unplaced"/>: сводка называет обе причины своими числами, и чертёж обязан
        /// говорить те же.
        /// </summary>
        readonly NameSet notDrawn = new NameSet();

        /// <summary>Тела текущего вида без прочитанных положения или габарита (<see cref="GdmlGeometry.IsUnplaced"/>).</summary>
        readonly NameSet unplaced = new NameSet();

        /// <summary>Оболочки вокруг всей сцены (<see cref="GdmlOverview.IsEnvelope"/>): не рисуются, рамка — по их содержимому.</summary>
        readonly NameSet envelopes = new NameSet();

        /// <summary>
        /// (fixGdml2, третий заход) Тома текущего вида, чьё содержимое читатель не разместил (`&lt;paramvol&gt;`, `&lt;replicavol&gt;`
        /// вдоль rho/phi, внешний файл): сам том нарисован, а внутри пусто — без подписи пустая коробка выглядела бы правдой.
        /// </summary>
        readonly NameSet contentHidden = new NameSet();

        /// <summary>Что именно не размещено в томах <see cref="contentHidden"/>, по разу.</summary>
        readonly SortedSet<string> contentReasons = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>Тела текущего вида, точного сечения которых здесь не построить.</summary>
        readonly NameSet noSection = new NameSet();

        /// <summary>Тела текущего вида за пределом глубины или работы: не нарисованы.</summary>
        readonly NameSet tooComplex = new NameSet();

        /// <summary>Тела текущего вида, у которых плоскость идёт мимо оси: на чертеже хорда, а не профиль.</summary>
        readonly NameSet offAxis = new NameSet();

        /// <summary>Объединения текущего вида: подпись говорит, что это одно тело с общим контуром.</summary>
        readonly NameSet unions = new NameSet();

        /// <summary>Сетки текущего вида без заливки: поверхность не замкнута, области сечения нет.</summary>
        readonly NameSet linesOnly = new NameSet();

        /// <summary>Тела текущего вида вне рамки: во много раз больше всего остального.</summary>
        readonly NameSet trimmed = new NameSet();

        /// <summary>Сколько фигур текущего вида действительно нарисовано или слишком мелко, чтобы быть видимым.</summary>
        int shapesDrawn;

        /// <summary>Сколько частей вычитаний уже взял текущий вид; предел — <see cref="MaxViewParts"/>.</summary>
        long viewParts;

        /// <summary>Сколько работы уже взял текущий вид; предел — <see cref="MaxViewWork"/>.</summary>
        long viewWork;

        /// <summary>Сколько вершин вычитаний уже взял текущий вид; предел — <see cref="MaxViewVertices"/>.</summary>
        long viewVertices;

        /// <summary>Глубина и число частей тела; память обхода живёт, пока не сменилась геометрия.</summary>
        readonly Dictionary<GdmlSolid, ShapeCost> shapeCosts = new Dictionary<GdmlSolid, ShapeCost>();

        /// <summary>Сколько областей реально собрано за последнюю отрисовку. Читает проба: память обхода работает.</summary>
        long regionsBuilt;

        /// <summary>Заголовки видов последней отрисовки. Читает проба.</summary>
        readonly List<string> paintTitles = new List<string>();

        /// <summary>О каком исключении заслона уже сказано: заслон срабатывает на каждую перерисовку.</summary>
        string reportedFailure;

        /// <summary>Подписи-отказы последней отрисовки, в порядке вывода. Читает проба `GdmlSketchProbeA304`.</summary>
        readonly List<string> paintNotes = new List<string>();

        /// <summary>Исключение, дошедшее до последнего заслона в OnPaint; null — заслон не понадобился.</summary>
        string paintFailure;

        public GdmlSketch()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Canvas;
        }

        Font LabelFont
        {
            get
            {
                if (this.labelFont == null || !ReferenceEquals(this.labelFontOf, this.Font))
                {
                    if (this.labelFont != null)
                    {
                        this.labelFont.Dispose();
                    }

                    this.labelFontOf = this.Font;
                    this.labelFont = new Font(this.Font, FontStyle.Bold);
                }

                return this.labelFont;
            }
        }

        public GdmlGeometry Geometry
        {
            get { return this.geometry; }
        }

        public void SetGeometry(GdmlGeometry value)
        {
            this.geometry = value;
            this.overview = null;
            this.commonScale = 0.0;
            this.reservedNotes[0] = this.reservedNotes[1] = 0f;
            this.shapeCosts.Clear();
            this.meshIds.Clear();
            this.calloutPlaces.Clear();
            this.reportedFailure = null;
            this.marks = null;
            this.highlight = null;
            this.Invalidate();
        }

        /// <summary>
        /// (`AMBER301`, дизайнер 11.11.2026) Что из файла взято в расчёт: выноски чертежа равны ролям блока «Сопоставление»
        /// (кристалл, проба, защита), стрелка «к пробе», плашка отказа. null — раскладки нет: выноски по ролям, узнанным
        /// программой. Ставится после <see cref="SetGeometry"/>, тот её сбрасывает.
        /// </summary>
        public void SetMarks(GdmlSketchMarks value)
        {
            this.marks = value;
            this.calloutPlaces.Clear();
            this.Invalidate();
        }

        GdmlSketchMarks marks;

        /// <summary>
        /// (`AMBER301`) Маленький чертёж окна импорта: тот же рисунок без обвязки — без размерных линий габарита (линейка
        /// масштаба остаётся), без строки пояснения под заголовком, выноски без размеров, легенда без граммов (массы стоят
        /// в списках слева). Геометрия та же: человек не должен усомниться, что это тот же файл.
        /// </summary>
        public bool Compact
        {
            get { return this.compact; }
            set
            {
                this.compact = value;
                this.calloutPlaces.Clear();
                this.Invalidate();
            }
        }

        bool compact;

        /// <summary>Пояснения видов последней отрисовки: где проходит плоскость, числом. Всплывающая подсказка; читает проба.</summary>
        readonly List<string> paintDetails = new List<string>();

        ToolTip planeTip;
        string planeTipText;

        /// <summary>
        /// Координаты плоскости разреза — во всплывающей подсказке вида (решение Amber 11.11.2026 вопросником, дословно:
        /// «Простые слова, координаты в подсказке (Рекомендую)»). Блоком, перенос по словам (`AMBER263`).
        /// </summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string text = null;
            for (int i = 0; i < this.paintSlots.Count && i < this.paintDetails.Count; i++)
            {
                if (this.paintSlots[i].Panel.Contains(e.Location))
                {
                    text = this.paintDetails[i];
                }
            }

            if (text == this.planeTipText)
            {
                return;
            }

            this.planeTipText = text;
            if (this.planeTip == null)
            {
                this.planeTip = new ToolTip();
            }

            this.planeTip.SetToolTip(this, text == null ? null : AppUi.WrapWords(text, AppUi.TipWidth));
        }

        /// <summary>Сработал последний заслон; причина — аргумент. Журнал ведёт форма.</summary>
        public event Action<string> DrawingFailed;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.labelFont != null)
                {
                    this.labelFont.Dispose();
                }

                if (this.planeTip != null)
                {
                    this.planeTip.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            this.paintNotes.Clear();
            this.paintTitles.Clear();
            this.paintDetails.Clear();
            this.paintMarks.Clear();
            this.paintCallouts.Clear();
            this.paintScales.Clear();
            this.paintSlots.Clear();
            this.paintFailure = null;
            this.regionsBuilt = 0;

            // ⛔ Последний заслон, а не защита: защита — рамка и зажим координат ниже, и проба
            // судит их отдельно. Исключение отсюда — окно ошибки приложения на каждую
            // перерисовку (`AMBER292`), поэтому до человека доходит текст, а не окно.
            try
            {
                graphics.Clear(Canvas);
                this.PaintContent(graphics);
            }
            catch (Exception ex)
            {
                this.paintFailure = ex.GetType().Name + ": " + ex.Message;
                this.PaintFailed(graphics);
                this.ReportFailure();
            }
        }

        void PaintFailed(Graphics graphics)
        {
            try
            {
                graphics.ResetClip();
                graphics.Clear(Canvas);
                this.DrawCentered(graphics, GdmlText.T("GdmlSketchFailed", "The drawing was not built"),
                                  this.ClientRectangle);
            }
            catch (Exception ex)
            {
                // Поверхность рисования сама негодна: сказать негде, но и падать незачем.
                this.paintFailure += "; " + ex.GetType().Name;
            }
        }

        void ReportFailure()
        {
            if (this.paintFailure == this.reportedFailure)
            {
                return;
            }

            this.reportedFailure = this.paintFailure;
            Trace.WriteLine("GdmlSketch: " + this.paintFailure);
            Action<string> handler = this.DrawingFailed;
            if (handler != null)
            {
                handler(this.paintFailure);
            }
        }

        void DrawCentered(Graphics graphics, string text, Rectangle rect)
        {
            this.paintNotes.Add(text);
            using (StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            })
            {
                graphics.DrawString(text, this.LabelFont, Brushes.DimGray, rect, format);
            }
        }

        static readonly Brush InkBrush = new SolidBrush(Ink);
        static readonly Brush DimBrush = new SolidBrush(Dim);
        static readonly Brush NoteBrush = new SolidBrush(NoteInk);

        /// <summary>Как стоят подписи выносок: в полях сбоку от чертежа, сверху и снизу или выносок нет.</summary>
        enum CalloutMode
        {
            None,
            Columns,
            Rows,
        }

        /// <summary>Место одного вида: рамка, поле чертежа и всё, что о нём известно до рисования.</summary>
        sealed class ViewSlot
        {
            public bool Axial;
            public int Normal, U, V;
            public double Plane;
            public Rectangle Panel;

            /// <summary>Поле чертежа: заголовка, подписей и выносок здесь нет.</summary>
            public Rectangle Plot;

            /// <summary>Есть место для заголовка, подписей и размеров; иначе только чертёж.</summary>
            public bool Text;

            public CalloutMode Mode;
            public float LabelWidth, LabelHeight;
            public bool ShortLabels;
        }

        /// <summary>Подпись и её прямоугольник: одна дорога на все подписи, иначе приёмка их не увидит.</summary>
        readonly List<GeometrySketch.DimLine> paintDims = new List<GeometrySketch.DimLine>();
        readonly List<RectangleF> paintContours = new List<RectangleF>();

        /// <summary>Линии простановки последней отрисовки. Читает проба.</summary>
        public GeometrySketch.DimLine[] DimensionLines
        {
            get { return this.paintDims.ToArray(); }
        }

        /// <summary>Контуры видов последней отрисовки. Читает проба.</summary>
        public RectangleF[] ViewContours
        {
            get { return this.paintContours.ToArray(); }
        }

        void Mark(Graphics graphics, string text, Font font, Brush brush, float x, float y, SizeF size)
        {
            graphics.DrawString(text, font, brush, x, y);
            this.paintMarks.Add(new SketchMark { Bounds = new RectangleF(x, y, size.Width, size.Height), Text = text });
        }

        void PaintContent(Graphics graphics)
        {
            if (this.geometry == null || !string.IsNullOrEmpty(this.geometry.FatalError)
                || this.geometry.Volumes.Count == 0)
            {
                string text = GdmlText.T("GdmlSketchNoGeometry", "No GDML geometry");
                SizeF size = graphics.MeasureString(text, this.LabelFont);
                graphics.DrawString(text, this.LabelFont, Brushes.DimGray,
                    (this.ClientSize.Width - size.Width) / 2.0f,
                    (this.ClientSize.Height - size.Height) / 2.0f);
                return;
            }

            // Подпись-отказ стоит над чертежом, а что на нём не нарисовано, известно только после
            // рисования. Поэтому место берётся по прошлой отрисовке и при расхождении вид рисуется
            // ещё раз; при обычной перерисовке и в файле без подписей второго прохода нет.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                graphics.Clear(Canvas);
                this.paintNotes.Clear();
                this.paintTitles.Clear();
                this.paintDetails.Clear();
                this.paintMarks.Clear();
                this.paintDims.Clear();
                this.paintContours.Clear();
                this.paintCallouts.Clear();
                this.paintScales.Clear();
                this.paintSlots.Clear();
                this.regionsBuilt = 0;
                this.paintBanner = RectangleF.Empty;

                List<LegendCell> legend;
                float legendHeight = this.LayoutLegend(graphics, out legend);
                Rectangle area = new Rectangle(0, 0, this.ClientSize.Width, this.ClientSize.Height - (int)Math.Ceiling(legendHeight));
                this.Arrange(graphics, area);
                foreach (ViewSlot slot in this.paintSlots)
                {
                    this.DrawView(graphics, slot);
                }

                this.DrawBanner(graphics, area);
                this.DrawLegend(graphics, legend, area.Bottom, legendHeight);

                bool settled = true;
                for (int i = 0; i < 2; i++)
                {
                    if (Math.Abs(this.neededNotes[i] - this.reservedNotes[i]) > 0.5f)
                    {
                        this.reservedNotes[i] = this.neededNotes[i];
                        settled = false;
                    }
                }

                if (settled)
                {
                    break;
                }
            }
        }

        // ------------------------------------------------------------------
        // Раскладка
        // ------------------------------------------------------------------

        const int ViewGap = 8;
        const int DimStrip = 20;
        const int ScaleStrip = 20;
        const int LeftStrip = 28;
        const int RightStrip = 4;

        /// <summary>
        /// (`AMBER324`) Левая полоса этой раскладки. Число вертикального габарита теперь горизонтально и стоит слева от
        /// линии, поэтому полоса — по его ширине (`Arrange`), не меньше прежней `LeftStrip`: повёрнутому хватало высоты строки.
        /// </summary>
        int leftStrip = LeftStrip;

        ViewSlot NewSlot(bool axial)
        {
            ViewSlot slot = new ViewSlot { Axial = axial };
            ViewAxes(axial, this.geometry.Axis, out slot.Normal, out slot.U, out slot.V);
            slot.Plane = axial ? this.AxialPlane(slot.Normal) : this.geometry.EndPlane;
            return slot;
        }

        /// <summary>Размах тел в координатах вида; false — считать не по чему, рамка скажет почему.</summary>
        bool Extent(ViewSlot slot, out double du, out double dv)
        {
            double umin = double.MaxValue, umax = double.MinValue, vmin = double.MaxValue, vmax = double.MinValue;
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null || !volume.BoundsKnown || this.Overview.IsOutOfFrame(volume))
                {
                    continue;
                }

                umin = Math.Min(umin, volume.Min[slot.U]);
                umax = Math.Max(umax, volume.Max[slot.U]);
                vmin = Math.Min(vmin, volume.Min[slot.V]);
                vmax = Math.Max(vmax, volume.Max[slot.V]);
            }

            du = Math.Max(umax - umin, 1e-6);
            dv = Math.Max(vmax - vmin, 1e-6);
            return umin <= umax && vmin <= vmax && IsFinite(du) && IsFinite(dv);
        }

        /// <summary>
        /// Тесно: раздел сводки сдвинут до предела в самом малом окне. Пояснение под заголовком и лишние строки
        /// легенды уступают чертежу: они есть в сводке, а чертежа больше нигде нет.
        /// </summary>
        bool Tight
        {
            get { return this.ClientSize.Width < 420 || this.ClientSize.Height < 300; }
        }

        float HeaderHeight(Graphics graphics, ViewSlot slot, float width)
        {
            float wrap = Math.Max(10f, width - 16f);
            float hint = this.Tight || this.compact ? 0f : graphics.MeasureString(this.HintOf(slot), this.Font, (int)wrap).Height;
            return 6f + graphics.MeasureString(this.TitleOf(slot), this.LabelFont, (int)wrap).Height
                 + hint
                 + this.reservedNotes[slot.Axial ? 0 : 1] + 2f;
        }

        /// <summary>
        /// Места двух видов. Перебор: рядом или друг под другом, выноски в полях сбоку или сверху и снизу;
        /// выбирается, где чертёж крупнее. Масштаб у видов один, как у двух видов чертежа редактора
        /// (`AMBER282`): рядом места делятся по размаху, а не пополам.
        /// </summary>
        void Arrange(Graphics graphics, Rectangle area)
        {
            ViewSlot axial = this.NewSlot(true);
            ViewSlot end = this.NewSlot(false);
            this.paintSlots.Add(axial);
            this.paintSlots.Add(end);
            this.commonScale = 0.0;
            axial.Mode = end.Mode = CalloutMode.None;

            double uA, vA, uE, vE;
            bool known = this.Extent(axial, out uA, out vA) & this.Extent(end, out uE, out vE);
            bool roomy = area.Width >= 200 && area.Height >= 120;
            axial.Text = end.Text = roomy;

            // (`AMBER324`) Полоса слева — под горизонтальное число вертикального габарита: линия первого ряда стоит на
            // `DimFirstPx` левее тела, тело не ближе `FrameMargin` к полосе, число на 4 точки левее линии, и рамка вида.
            // В тесном окне полосу не расширять: чертёж мельчал на треть (92 → 59 точек в окне 320×367); там число встаёт
            // со стороны тела на продолжении линии, а нет места — размера нет, как было (`DrawDimensions`).
            this.leftStrip = LeftStrip;
            if (known && !this.Tight && !this.compact)
            {
                string unit = GdmlText.T("GdmlUnitMm", "mm");
                float widest = Math.Max(graphics.MeasureString(Fmt(vA) + " " + unit, this.Font).Width,
                                        graphics.MeasureString(Fmt(vE) + " " + unit, this.Font).Width);
                this.leftStrip = Math.Max(LeftStrip, (int)Math.Ceiling(widest + 4f + GeometrySketch.DimFirstPx - FrameMargin + 4f));
            }
            if (!known || !roomy)
            {
                this.SplitInHalf(graphics, axial, end, area, false);
                return;
            }

            // Выноски берут место у чертежа: при тесном окне сперва короткие, затем никаких.
            float labelWidth = 0f, labelHeight = 0f;
            bool shortLabels = false;
            double best = -1.0;
            bool bestSide = true;
            CalloutMode bestMode = CalloutMode.None;
            double extent = Math.Max(Math.Max(uA, vA), Math.Max(uE, vE));
            for (int detail = this.compact ? 1 : 2; detail >= 0; detail--)
            {
                labelWidth = labelHeight = 0f;
                shortLabels = detail == 1;
                if (detail > 0)
                {
                    this.MeasureCallouts(graphics, shortLabels, out labelWidth, out labelHeight);
                }

                // Тесно: только сверху и снизу. Сбоку линия идёт через число вертикального размера, а двигать его
                // при низком чертеже некуда.
                CalloutMode[] modes = labelWidth <= 0f
                    ? new[] { CalloutMode.None }
                    : this.Tight ? new[] { CalloutMode.Rows } : new[] { CalloutMode.Rows, CalloutMode.Columns };
                best = -1.0;
                foreach (bool side in new[] { true, false })
                {
                    foreach (CalloutMode mode in modes)
                    {
                        int wA, wE, hA, hE;
                        double scale = this.Fit(graphics, axial, end, area, side, mode, labelWidth, labelHeight, uA, vA, uE, vE, out wA, out wE, out hA, out hE);
                        if (scale > best * 1.02)
                        {
                            best = scale;
                            bestSide = side;
                            bestMode = mode;
                        }
                    }
                }

                if (best * extent >= (detail == 2 ? 170.0 : 110.0))
                {
                    break;
                }
            }

            axial.Mode = end.Mode = bestMode;
            axial.LabelWidth = end.LabelWidth = labelWidth;
            axial.LabelHeight = end.LabelHeight = labelHeight;
            axial.ShortLabels = end.ShortLabels = shortLabels;

            // Один из видов при общем масштабе ушёл бы в крапинку (длинное и тонкое тело): масштабы
            // разные, у каждого своя метка. Это честно, а крапинка ничего бы не показала.
            if (best * Math.Max(uA, vA) < 40.0 || best * Math.Max(uE, vE) < 40.0)
            {
                this.SplitInHalf(graphics, axial, end, area, true);
                return;
            }

            {
                int wA, wE, hA, hE;
                this.Fit(graphics, axial, end, area, bestSide, bestMode, labelWidth, labelHeight, uA, vA, uE, vE, out wA, out wE, out hA, out hE);
                if (bestSide)
                {
                    axial.Panel = new Rectangle(area.Left, area.Top, wA, area.Height);
                    end.Panel = new Rectangle(area.Left + wA + ViewGap, area.Top, area.Width - wA - ViewGap, area.Height);
                }
                else
                {
                    axial.Panel = new Rectangle(area.Left, area.Top, area.Width, hA);
                    end.Panel = new Rectangle(area.Left, area.Top + hA + ViewGap, area.Width, area.Height - hA - ViewGap);
                }
            }

            this.PlacePlot(graphics, axial);
            this.PlacePlot(graphics, end);
            double common = Math.Min(
                Math.Min((axial.Plot.Width - 2.0 * FrameMargin) / uA, (axial.Plot.Height - 2.0 * FrameMargin) / vA),
                Math.Min((end.Plot.Width - 2.0 * FrameMargin) / uE, (end.Plot.Height - 2.0 * FrameMargin) / vE));
            this.commonScale = common > 0.0 ? common : 0.0;
        }

        void SplitInHalf(Graphics graphics, ViewSlot axial, ViewSlot end, Rectangle area, bool roomForText)
        {
            int leftWidth = (area.Width - ViewGap) / 2;
            axial.Panel = new Rectangle(area.Left, area.Top, Math.Max(0, leftWidth), area.Height);
            end.Panel = new Rectangle(area.Left + leftWidth + ViewGap, area.Top, Math.Max(0, area.Width - leftWidth - ViewGap), area.Height);
            axial.Mode = roomForText ? axial.Mode : CalloutMode.None;
            end.Mode = axial.Mode;
            this.PlacePlot(graphics, axial);
            this.PlacePlot(graphics, end);
        }

        void PlacePlot(Graphics graphics, ViewSlot slot)
        {
            int left, right, top, bottom;
            Strips(slot.Mode, slot.Text, slot.LabelWidth, slot.LabelHeight, this.leftStrip, !this.compact, out left, out right, out top, out bottom);
            int header = slot.Text ? (int)Math.Ceiling(this.HeaderHeight(graphics, slot, slot.Panel.Width)) : 0;
            slot.Plot = new Rectangle(slot.Panel.Left + left, slot.Panel.Top + header + top,
                                      Math.Max(0, slot.Panel.Width - left - right),
                                      Math.Max(0, slot.Panel.Height - header - top - bottom));
        }

        static void Strips(CalloutMode mode, bool text, float labelWidth, float labelHeight, int leftStrip, bool dimensions,
                           out int left, out int right, out int top, out int bottom)
        {
            left = text ? leftStrip : 0;
            right = text ? RightStrip : 0;
            top = 0;
            bottom = text ? (dimensions ? DimStrip : 0) + ScaleStrip : 0;
            int w = (int)Math.Ceiling(labelWidth) + 14;
            int h = (int)Math.Ceiling(labelHeight) + 8;
            if (mode == CalloutMode.Columns)
            {
                left += w;
                right += w;
            }
            else if (mode == CalloutMode.Rows)
            {
                top += h;
                bottom += h;
            }
        }

        /// <summary>Наибольший общий масштаб при такой раскладке; заодно размеры мест.</summary>
        double Fit(Graphics graphics, ViewSlot axial, ViewSlot end, Rectangle area, bool side, CalloutMode mode,
                   float labelWidth, float labelHeight, double uA, double vA, double uE, double vE,
                   out int wA, out int wE, out int hA, out int hE)
        {
            int left, right, top, bottom;
            Strips(mode, true, labelWidth, labelHeight, this.leftStrip, !this.compact, out left, out right, out top, out bottom);
            double margins = 2.0 * FrameMargin;
            double insetX = left + right + margins;
            if (side)
            {
                double room = area.Width - ViewGap;
                double scale = (room - 2.0 * insetX) / (uA + uE);
                double widthEnd = insetX + scale * uE;
                double least = Math.Min(230.0, room / 2.0);
                if (widthEnd < least)
                {
                    widthEnd = least;
                    scale = (room - widthEnd - insetX) / uA;
                }
                else if (room - widthEnd < least)
                {
                    widthEnd = room - least;
                    scale = (widthEnd - insetX) / uE;
                }

                wE = (int)widthEnd;
                wA = (int)room - wE;
                hA = hE = area.Height;
                double headerA = this.HeaderHeight(graphics, axial, wA);
                double headerE = this.HeaderHeight(graphics, end, wE);
                double plotA = area.Height - headerA - top - bottom - margins;
                double plotE = area.Height - headerE - top - bottom - margins;
                return Math.Min(scale, Math.Min(plotA / vA, plotE / vE));
            }
            else
            {
                wA = wE = area.Width;
                double headerA = this.HeaderHeight(graphics, axial, area.Width);
                double headerE = this.HeaderHeight(graphics, end, area.Width);
                double insetA = headerA + top + bottom + margins;
                double insetE = headerE + top + bottom + margins;
                double room = area.Height - ViewGap;
                double scale = (room - insetA - insetE) / (vA + vE);
                double heightEnd = insetE + scale * vE;
                double least = Math.Min(150.0, room / 2.0);
                if (heightEnd < least)
                {
                    heightEnd = least;
                    scale = (room - heightEnd - insetA) / vA;
                }
                else if (room - heightEnd < least)
                {
                    heightEnd = room - least;
                    scale = (heightEnd - insetE) / vE;
                }

                hE = (int)heightEnd;
                hA = (int)room - hE;
                return Math.Min(scale, (area.Width - insetX) / Math.Max(uA, uE));
            }
        }

        // ------------------------------------------------------------------
        // Вид
        // ------------------------------------------------------------------

        /// <summary>
        /// Заголовок вида простыми словами (решение Amber 11.11.2026 вопросником, дословно: «Простые слова, координаты в
        /// подсказке (Рекомендую)»): координаты плоскости — в <see cref="DetailOf"/>, всплывающей подсказкой.
        /// </summary>
        string TitleOf(ViewSlot slot)
        {
            return slot.Axial
                ? GdmlText.T("GdmlSketchAxialTitle", "Side view, cut open")
                : GdmlText.T("GdmlSketchEndTitle", "Cross-section");
        }

        /// <summary>Пояснение вида одной короткой строкой; линия A–A — единственная связь двух видов.</summary>
        string HintOf(ViewSlot slot)
        {
            return slot.Axial
                ? GdmlText.T("GdmlSketchAlongShort", "Cut along the axis.")
                : GdmlText.T("GdmlSketchAcrossShort", "Cut across, along line A-A.");
        }

        /// <summary>
        /// Где проходит плоскость, числом и полной фразой: всплывающая подсказка вида. Начинается теми же словами, что
        /// заголовок вида («Вид сбоку, в разрезе (y = 0)», «Поперечный разрез (z = 0 мм)»; критик 12.11.2026, Р2).
        /// </summary>
        string DetailOf(ViewSlot slot)
        {
            return this.PlaneOf(slot) + ". " + this.PlaneHintOf(slot);
        }

        string PlaneOf(ViewSlot slot)
        {
            return slot.Axial
                ? string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchAxial", "Side view, cut open ({0} = {1})"),
                    AxisLetter(slot.Normal),
                    slot.Plane == 0.0 ? "0" : GdmlText.N(slot.Plane) + " " + GdmlText.T("GdmlUnitMm", "mm"))
                : string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchEnd", "Cross-section ({0} = {1} mm)"),
                    this.geometry.Axis, GdmlText.N(slot.Plane));
        }

        string PlaneHintOf(ViewSlot slot)
        {
            if (slot.Axial)
            {
                // «Вдоль», а не «через ось»: плоскость бывает и мимо оси тела, и об этом рядом подпись.
                return GdmlText.T("GdmlSketchAlongHint", "A plane along the detector axis. Line A-A: where the cross-section is cut.");
            }

            return this.geometry.CrystalName.Length > 0
                ? GdmlText.T("GdmlSketchEndHint", "Cut across the axis along line A-A, through the crystal centre.")
                : GdmlText.T("GdmlSketchEndHintMiddle", "Cut across the axis along line A-A, through the middle of the scene.");
        }

        void DrawView(Graphics graphics, ViewSlot slot)
        {
            Rectangle rect = slot.Panel;
            int index = slot.Axial ? 0 : 1;
            this.neededNotes[index] = 0f;
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            using (Pen border = new Pen(Divider, 1.0f))
            {
                graphics.DrawRectangle(border, rect.Left, rect.Top, rect.Width - 1, rect.Height - 1);
            }

            ViewFrame frame = this.MakeFrame(slot.Plot, slot.U, slot.V);
            if (frame != null && !IsFinite(slot.Plane))
            {
                frame = null;
                this.frameProblem = OutOfRangeText();
            }

            if (frame == null)
            {
                // Нет места — молчать: подпись в такой щели всё равно не прочесть.
                if (this.frameProblem != null)
                {
                    this.DrawCentered(graphics, this.frameProblem, rect);
                }

                return;
            }

            this.notDrawn.Clear();
            this.unplaced.Clear();
            this.envelopes.Clear();
            this.contentHidden.Clear();
            this.contentReasons.Clear();
            this.noSection.Clear();
            this.tooComplex.Clear();
            this.unions.Clear();
            this.offAxis.Clear();
            this.linesOnly.Clear();
            this.trimmed.Clear();
            this.shapesDrawn = 0;
            this.viewParts = 0;
            this.viewWork = 0;
            this.viewVertices = 0;

            this.paintTitles.Add(this.TitleOf(slot));
            this.paintDetails.Add(this.DetailOf(slot));

            // Вид не вылезает на соседний и на подписи: второе тело вычитания и начало координат бывают вне поля.
            graphics.SetClip(slot.Plot);
            try
            {
                foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
                {
                    this.DrawVolume(graphics, frame, volume, slot.Normal, slot.U, slot.V, slot.Plane);
                }
            }
            finally
            {
                graphics.ResetClip();
            }

            float notesTop = rect.Top + 6f;
            if (slot.Text)
            {
                float wrap = Math.Max(10f, rect.Width - 16f);
                SizeF titleSize = graphics.MeasureString(this.TitleOf(slot), this.LabelFont, (int)wrap);
                graphics.DrawString(this.TitleOf(slot), this.LabelFont, InkBrush, new RectangleF(rect.Left + 8f, notesTop, wrap, titleSize.Height));
                this.paintMarks.Add(new SketchMark { Bounds = new RectangleF(rect.Left + 8f, notesTop, titleSize.Width, titleSize.Height), Text = this.TitleOf(slot) });
                notesTop += titleSize.Height;
                if (!this.Tight && !this.compact)
                {
                    SizeF hintSize = graphics.MeasureString(this.HintOf(slot), this.Font, (int)wrap);
                    graphics.DrawString(this.HintOf(slot), this.Font, DimBrush, new RectangleF(rect.Left + 8f, notesTop, wrap, hintSize.Height));
                    this.paintMarks.Add(new SketchMark { Bounds = new RectangleF(rect.Left + 8f, notesTop, hintSize.Width, hintSize.Height), Text = this.HintOf(slot) });
                    notesTop += hintSize.Height;
                }
            }

            // Сперва места выносок: число размера и буква A обходят их линии, а не наоборот.
            List<PlacedCallout> placed = slot.Mode != CalloutMode.None
                ? this.PlanCallouts(graphics, frame, slot)
                : new List<PlacedCallout>();
            this.DrawViewNotes(graphics, slot, index, notesTop);
            if (slot.Text)
            {
                // Стрелка «к пробе» — до буквы A и размеров: они обходят её, как выноски.
                this.DrawScale(graphics, frame, slot);
                RectangleF arrow = this.DrawSampleArrow(graphics, frame, slot, placed);
                this.DrawGuides(graphics, frame, slot, placed, arrow);
                if (!this.compact)
                {
                    this.DrawDimensions(graphics, frame, slot, placed);
                }
            }

            this.RenderCallouts(graphics, slot, placed);
        }

        /// <summary>
        /// (`AMBER301`, остаток `a301c` 12.11.2026) Просветы выносок. На снимках гида подпись «Detector» стояла под самой
        /// размерной линией «214 mm», её линия шла вплотную к числу, а «Детектор» окна импорта — вплотную к букве «A»:
        /// прежде всё это мерилось «не пересекается», то есть нулевым просветом.
        ///   * <see cref="CalloutBeyondDims"/> — подпись со стороны размера габарита (под чертежом и слева) стоит за его первым
        ///     рядом: в 12 точках от размерной линии и в 9 от вылета выносных (было 8 и 5);
        ///   * <see cref="CalloutMarkRoom"/> — просвет между подписью или линией выноски и буквой «A» линии разреза, числом размера.
        /// Места выносок считаются первыми; им уступают буква «A» (другая сторона линии) и число размера (вдоль своей линии),
        /// а ряд подписей над чертежом сам поднимается над буквой.
        /// </summary>
        const float CalloutMarkRoom = 4f;

        /// <summary>Отступ подписи от чертежа со стороны размера габарита: первый ряд размера, вылет выносных и просвет.</summary>
        const float CalloutBeyondDims = GeometrySketch.DimFirstPx + GeometrySketch.DimOverPx + 9f;

        /// <summary>Задевает ли линия какой-нибудь выноски этот прямоугольник; <paramref name="room"/> — просвет вокруг него.</summary>
        static bool LeaderHits(List<PlacedCallout> placed, RectangleF box, float room = 1f)
        {
            box.Inflate(room, room);
            foreach (PlacedCallout item in placed)
            {
                PointF a = item.From, b = item.Anchor;
                if (ClipSegment(box, ref a, ref b))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Ось — тонким штрихпунктиром ПОВЕРХ тел: под телами её не было видно там, где она нужна.
        /// Линия A–A на разрезе показывает, где снят торец: тот же приём, что на машиностроительном чертеже.
        /// </summary>
        /// <summary>
        /// (`AMBER301`) Стрелка «к пробе» у грани, обращённой к пробе, — только когда пробы в файле нет (решение Amber
        /// 11.11.2026 вопросником, дословно: «Только если пробы в файле нет (Рекомендую)»): отвечает на список «Сторона к
        /// пробе» и переезжает при его смене. На виде, где сторона смотрит на зрителя или от него, стрелки нет. Место — у
        /// грани детектора (при защите из файла — в её полости), в стороне от оси и линии A–A, свободное от выносок,
        /// подписей и тел, взятых в расчёт; у стороны, где стоит размер габарита, — вдоль соседней грани. Нет места —
        /// стрелки нет, как у размера. Возвращает занятую рамку (пусто — не нарисована).
        /// </summary>
        RectangleF DrawSampleArrow(Graphics graphics, ViewFrame frame, ViewSlot slot, List<PlacedCallout> placed)
        {
            GdmlSketchMarks m = this.marks;
            if (m == null || m.SampleAxis < 0 || m.SampleSign == 0 || m.Crystal == null || !m.Crystal.BoundsKnown
                || (m.SampleAxis != slot.U && m.SampleAxis != slot.V))
            {
                return RectangleF.Empty;
            }

            bool alongU = m.SampleAxis == slot.U;
            double cu = 0.5 * (m.Crystal.Min[slot.U] + m.Crystal.Max[slot.U]);
            double cv = 0.5 * (m.Crystal.Min[slot.V] + m.Crystal.Max[slot.V]);
            if (!IsFinite(cu) || !IsFinite(cv))
            {
                return RectangleF.Empty;
            }

            // Грань ДЕТЕКТОРА со стороны пробы, а не край всего взятого: при защите из файла и пробе вручную стрелка
            // стоит у детектора, в полости защиты, — там, куда человек положит пробу (критик 12.11.2026, Р3б: прежде
            // краем считалась наружная грань защиты, места за ней нет, и стрелки не было вовсе).
            double reach = m.SampleSign > 0 ? m.Crystal.Max[m.SampleAxis] : m.Crystal.Min[m.SampleAxis];
            if (m.DetectorMin != null && m.DetectorMax != null)
            {
                reach = m.SampleSign > 0 ? Math.Max(reach, m.DetectorMax[m.SampleAxis]) : Math.Min(reach, m.DetectorMin[m.SampleAxis]);
            }

            if (!IsFinite(reach))
            {
                return RectangleF.Empty;
            }

            reach = m.SampleSign > 0 ? Math.Min(reach, alongU ? frame.UMax : frame.VMax) : Math.Max(reach, alongU ? frame.UMin : frame.VMin);
            PointF edge = frame.ToScreen(alongU ? reach : cu, alongU ? cv : reach);
            PointF inner = frame.ToScreen(cu, cv);
            float dx = alongU ? Math.Sign(edge.X - inner.X) : 0f;
            float dy = alongU ? 0f : Math.Sign(edge.Y - inner.Y);
            if (dx == 0f && dy == 0f)
            {
                return RectangleF.Empty;
            }

            const float Gap = 3f, Length = 18f, Head = 5f, Beside = 9f;
            string text = GdmlText.T("GdmlSketchToSample", "to sample");
            SizeF size = graphics.MeasureString(text, this.Font);
            RectangleF drawn = frame.Drawn;
            float axisRow = slot.Axial && frame.VMin <= 0.0 && frame.VMax >= 0.0 ? frame.ToScreen(frame.UMin, 0.0).Y : float.NaN;
            float cutColumn = slot.Axial && IsFinite(this.geometry.EndPlane) ? frame.ToScreen(this.geometry.EndPlane, 0.0).X : float.NaN;

            // С этой стороны стоит размер габарита (вертикальный — слева, горизонтальный — снизу): стрелка наружу упёрлась бы
            // наконечником в размерную линию (RC-110, бок −X; критик 12.11.2026, п. 1.9). Сперва место вдоль соседней грани.
            bool towardDims = !this.compact && ((alongU && dx < 0f) || (!alongU && dy > 0f));

            // Попытки: стрелка (откуда, куда) и места её подписи по порядку.
            List<KeyValuePair<PointF[], RectangleF[]>> tries = new List<KeyValuePair<PointF[], RectangleF[]>>();
            Action outside = () =>
            {
                // Наружу от грани; у размерной стороны короче: до размерной линии (`DimFirstPx`) остаётся просвет.
                float length = towardDims ? Math.Min(Length, GeometrySketch.DimFirstPx - Gap - 8f) : Length;
                foreach (float shift in new[] { 0f, 14f, -14f, 28f, -28f, 42f, -42f })
                {
                    PointF from = new PointF(edge.X + dx * Gap + (alongU ? 0f : shift), edge.Y + dy * Gap + (alongU ? shift : 0f));
                    PointF to = new PointF(from.X + dx * length, from.Y + dy * length);

                    // На оси и на линии A–A стрелка потерялась бы.
                    if ((alongU && Math.Abs(from.Y - axisRow) < 7f) || (!alongU && Math.Abs(from.X - cutColumn) < 10f))
                    {
                        continue;
                    }

                    // Подпись: у горизонтальной стрелки над или под ней, от грани наружу (не на тело); у вертикальной —
                    // справа или слева.
                    RectangleF[] labels = new RectangleF[2];
                    for (int k = 0; k < 2; k++)
                    {
                        labels[k] = alongU
                            ? new RectangleF(dx > 0f ? from.X : from.X - size.Width, k == 0 ? from.Y - Head - size.Height + 2f : from.Y + Head - 2f, size.Width, size.Height)
                            : new RectangleF(k == 0 ? from.X + Head - 1f : from.X - Head + 1f - size.Width, Math.Min(from.Y, to.Y) + (length - size.Height) / 2f, size.Width, size.Height);
                    }

                    tries.Add(new KeyValuePair<PointF[], RectangleF[]>(new[] { from, to }, labels));
                }
            };
            Action beside = () =>
            {
                // Вдоль соседней грани, наконечник у грани пробы: только когда рядом с детектором на чертеже ничего нет
                // (его грани — края чертежа), иначе стрелка в стороне от детектора читалась бы про другое тело.
                if (m.DetectorMin == null || m.DetectorMax == null)
                {
                    return;
                }

                int across = alongU ? slot.V : slot.U;
                PointF low = frame.ToScreen(alongU ? reach : m.DetectorMin[across], alongU ? m.DetectorMin[across] : reach);
                PointF high = frame.ToScreen(alongU ? reach : m.DetectorMax[across], alongU ? m.DetectorMax[across] : reach);
                float near = alongU ? Math.Min(low.Y, high.Y) : Math.Min(low.X, high.X), far = alongU ? Math.Max(low.Y, high.Y) : Math.Max(low.X, high.X);
                if (Math.Abs(near - (alongU ? drawn.Top : drawn.Left)) > 2f || Math.Abs(far - (alongU ? drawn.Bottom : drawn.Right)) > 2f)
                {
                    return;
                }

                foreach (bool first in new[] { true, false })
                {
                    // Слева от тела стоит вертикальный размер габарита, под телом — горизонтальный: вертикальной стрелке
                    // слева места нет (она пересекла бы выносную линию, и размер пропал бы), а под телом помещается только
                    // сама стрелка с подписью в одну строку — до размерной линии остаётся просвет.
                    bool dims = !this.compact && !first == alongU;
                    if (dims && !alongU)
                    {
                        continue;
                    }

                    float line = first ? near - Beside : far + Beside;
                    PointF to = alongU ? new PointF(edge.X - dx * 8f, line) : new PointF(line, edge.Y - dy * 8f);
                    PointF from = new PointF(to.X - dx * Length, to.Y - dy * Length);
                    List<RectangleF> labels = new List<RectangleF>();
                    if (alongU)
                    {
                        // За хвостом стрелки, в одну строку с ней; иначе — второй строкой дальше от тела, от наконечника.
                        labels.Add(new RectangleF(dx > 0f ? from.X - 2f - size.Width : from.X + 2f, line - size.Height / 2f, size.Width, size.Height));
                        if (!dims)
                        {
                            labels.Add(new RectangleF(dx > 0f ? to.X - size.Width : to.X, first ? line - Head - size.Height + 1f : line + Head - 1f, size.Width, size.Height));
                        }
                    }
                    else
                    {
                        labels.Add(new RectangleF(first ? line - Head + 1f - size.Width : line + Head - 1f, Math.Min(from.Y, to.Y) + (Length - size.Height) / 2f, size.Width, size.Height));
                    }

                    tries.Add(new KeyValuePair<PointF[], RectangleF[]>(new[] { from, to }, labels.ToArray()));
                }
            };
            if (towardDims)
            {
                beside();
                outside();
            }
            else
            {
                outside();
                beside();
            }

            ViewContext view = new ViewContext { Frame = frame, Normal = slot.Normal, U = slot.U, V = slot.V, Plane = slot.Plane };
            Dictionary<GdmlVolumeInstance, Region> solids = new Dictionary<GdmlVolumeInstance, Region>();
            try
            {
                foreach (KeyValuePair<PointF[], RectangleF[]> attempt in tries)
                {
                    PointF from = attempt.Key[0], to = attempt.Key[1];

                    // Рамка стрелки: вдоль — ровно стрелка, поперёк — её наконечник. Запас вдоль залезал бы на тело и на
                    // выносную линию размера у того же края, и размер пропадал.
                    RectangleF shaft = alongU
                        ? RectangleF.FromLTRB(Math.Min(from.X, to.X), from.Y - Head, Math.Max(from.X, to.X), from.Y + Head)
                        : RectangleF.FromLTRB(from.X - Head, Math.Min(from.Y, to.Y), from.X + Head, Math.Max(from.Y, to.Y));
                    if (!this.NumberFree(shaft, slot, placed) || this.OnTakenSolid(view, shaft, solids))
                    {
                        continue;
                    }

                    foreach (RectangleF label in attempt.Value)
                    {
                        // Подпись не садится ни на взятое в расчёт тело, ни на ось, ни на линию A–A с её буквами.
                        if (!this.NumberFree(label, slot, placed) || this.OnTakenSolid(view, label, solids)
                            || (axisRow > label.Top - 1f && axisRow < label.Bottom + 1f)
                            || (alongU && cutColumn > label.Left - 12f && cutColumn < label.Right + 12f && (label.Top < drawn.Top || label.Bottom > drawn.Bottom))
                            // (`a301c`) У вертикальной стрелки подпись сбоку не ложится поперёк линии A–A вне тела: там её штрихи и
                            // буква «A», и букве не остаётся ни одной стороны (RC-110, торец: место освободил поднятый ряд выносок).
                            || (!alongU && cutColumn > label.Left && cutColumn < label.Right && (label.Top < drawn.Top || label.Bottom > drawn.Bottom)))
                        {
                            continue;
                        }

                        using (Pen pen = new Pen(Ink, 1.6f))
                        using (GraphicsPath head = new GraphicsPath())
                        {
                            graphics.DrawLine(pen, from, new PointF(to.X - dx * Head, to.Y - dy * Head));
                            head.AddPolygon(new[]
                            {
                                to,
                                new PointF(to.X - dx * 2f * Head - dy * Head, to.Y - dy * 2f * Head - dx * Head),
                                new PointF(to.X - dx * 2f * Head + dy * Head, to.Y - dy * 2f * Head + dx * Head),
                            });
                            graphics.FillPath(InkBrush, head);
                        }

                        // Подложка цвета холста: подпись бывает поверх бледной пробы или защиты, не взятых в расчёт.
                        using (Brush back = new SolidBrush(Color.FromArgb(0xC8, Canvas)))
                        {
                            graphics.FillRectangle(back, label.X + 1f, label.Y + 1f, label.Width - 2f, label.Height - 2f);
                        }

                        graphics.DrawString(text, this.Font, InkBrush, label.X, label.Y);

                        // Две рамки, а не общая: в углу между стрелкой и подписью место свободно, и размер его берёт.
                        this.paintMarks.Add(new SketchMark { Bounds = shaft, Text = "→" });
                        this.paintMarks.Add(new SketchMark { Bounds = label, Text = text });
                        return RectangleF.Union(shaft, label);
                    }
                }
            }
            finally
            {
                foreach (Region region in solids.Values)
                {
                    if (region != null)
                    {
                        region.Dispose();
                    }
                }

                view.ReleaseCache();
            }

            return RectangleF.Empty;
        }

        /// <summary>
        /// (`AMBER301`, критик 12.11.2026, п. 1.9) Рамка лежит на сечении тела, взятого в расчёт: стрелка «к пробе» и её
        /// подпись туда не садятся (на RC-110 подпись лежала поверх стенки корпуса). Бледное и газ не в счёт: под подписью
        /// у них подложка. Сечение считается только у тел, чей габарит задевает рамку, и один раз на отрисовку.
        /// </summary>
        bool OnTakenSolid(ViewContext view, RectangleF box, Dictionary<GdmlVolumeInstance, Region> solids)
        {
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null || volume.Solid == null || volume.Transform == null || !volume.BoundsKnown || !volume.PlacementKnown
                    || (this.pale != null && this.pale(volume)) || this.Overview.IsOutOfFrame(volume)
                    || this.Overview.RoleOf(volume) == GdmlRole.Gas || !IntersectsPlane(volume, view.Normal, view.Plane))
                {
                    continue;
                }

                PointF a = view.Frame.ToScreen(volume.Min[view.U], volume.Min[view.V]), b = view.Frame.ToScreen(volume.Max[view.U], volume.Max[view.V]);
                RectangleF bounds = RectangleF.FromLTRB(Math.Min(a.X, b.X) - 1f, Math.Min(a.Y, b.Y) - 1f, Math.Max(a.X, b.X) + 1f, Math.Max(a.Y, b.Y) + 1f);
                if (!bounds.IntersectsWith(box))
                {
                    continue;
                }

                Region region;
                if (!solids.TryGetValue(volume, out region))
                {
                    if (solids.Count >= 64)
                    {
                        // Слишком много тел у стрелки: считать дальше дорого, а место заведомо тесное.
                        return true;
                    }

                    view.Volume = volume;
                    region = this.AreaOf(view, volume);
                    solids[volume] = region;
                }

                if (region != null && region.IsVisible(box))
                {
                    return true;
                }
            }

            return false;
        }

        void DrawGuides(Graphics graphics, ViewFrame frame, ViewSlot slot, List<PlacedCallout> placed, RectangleF arrow)
        {
            RectangleF drawn = frame.Drawn;
            using (Pen axisPen = new Pen(GuideInk, 1.0f))
            {
                axisPen.DashStyle = DashStyle.DashDot;
                if (slot.Axial)
                {
                    if (frame.VMin <= 0.0 && frame.VMax >= 0.0)
                    {
                        PointF a = frame.ToScreen(frame.UMin, 0.0);
                        PointF b = frame.ToScreen(frame.UMax, 0.0);
                        graphics.DrawLine(axisPen, a.X - 8f, a.Y, b.X + 8f, b.Y);
                    }

                    double cut = this.geometry.EndPlane;
                    if (IsFinite(cut) && cut >= frame.UMin && cut <= frame.UMax)
                    {
                        float x = frame.ToScreen(cut, 0.0).X;
                        graphics.DrawLine(axisPen, x, drawn.Top, x, drawn.Bottom);
                        using (Pen end = new Pen(Ink, 1.6f))
                        {
                            graphics.DrawLine(end, x, drawn.Top - 8f, x, drawn.Top - 2f);
                            graphics.DrawLine(end, x, drawn.Bottom + 2f, x, drawn.Bottom + 8f);
                        }

                        SizeF size = graphics.MeasureString("A", this.LabelFont);
                        // Справа от линии, а если там выноска — слева.
                        float yTop = drawn.Top - size.Height - 1f, yBottom = drawn.Bottom + 1f;
                        RectangleF atTop = new RectangleF(x + 2f, yTop, size.Width, size.Height), atBottom = new RectangleF(x + 2f, yBottom, size.Width, size.Height);
                        // (`a301c`) Не вплотную к линии выноски: та сторона, где просвет до неё больше (при равном — справа).
                        // Прежде буква уходила налево, только когда линия выноски шла прямо через неё, и вставала в точке от линии.
                        Func<RectangleF, float> letterAt = right =>
                        {
                            RectangleF left = new RectangleF(x - 2f - size.Width, right.Y, size.Width, size.Height);
                            // Рамки подписей здесь не спрашиваются: ряд над чертежом уже поднят над буквой (`PlaceSide`), а подписи
                            // под чертежом и в столбцах стоят от неё дальше 8 точек. Слагаемое с рамками было недостижимо (критик
                            // 12.11.2026, мутант x1) и снято; исход держит проверка пробы «подпись не ближе 4 точек к букве».
                            Func<RectangleF, float, bool> busy = (box, room) => LeaderHits(placed, box, room) || arrow.IntersectsWith(box);
                            for (float room = CalloutMarkRoom; room >= 1f; room -= 1f)
                            {
                                if (!busy(right, room))
                                {
                                    return right.X;
                                }

                                if (!busy(left, room))
                                {
                                    return left.X;
                                }
                            }

                            return left.X;
                        };
                        float xTop = letterAt(atTop), xBottom = letterAt(atBottom);
                        this.Mark(graphics, "A", this.LabelFont, InkBrush, xTop, yTop, size);
                        this.Mark(graphics, "A", this.LabelFont, InkBrush, xBottom, yBottom, size);
                    }
                }
                else if (frame.UMin <= 0.0 && frame.UMax >= 0.0 && frame.VMin <= 0.0 && frame.VMax >= 0.0)
                {
                    PointF center = frame.ToScreen(0.0, 0.0);
                    graphics.DrawLine(axisPen, center.X - 10f, center.Y, center.X + 10f, center.Y);
                    graphics.DrawLine(axisPen, center.X, center.Y - 10f, center.X, center.Y + 10f);
                }
            }
        }

        /// <summary>Габарит видимого: снизу и слева, стрелками и числом, как размеры чертежа редактора.</summary>
        void DrawDimensions(Graphics graphics, ViewFrame frame, ViewSlot slot, List<PlacedCallout> placed)
        {
            RectangleF drawn = frame.Drawn;
            if (drawn.Width < 40f && drawn.Height < 40f)
            {
                return;
            }

            // (`AMBER307`, Amber 06.11.2026 вопросником, дословно: «Да, теми же правилами (Рекомендую)».) Те же правила ГОСТ 2.307-2011,
            // что у чертежа редактора, те же ряды, вылет и стрелки (`GeometrySketch.DimPitchPx` и соседи): число над линией,
            // у вертикальной слева от неё, НЕ повёрнуто (`AMBER324`); нет места над линией — стрелки снаружи, число на продолжении.
            string unit = GdmlText.T("GdmlUnitMm", "mm");
            this.paintContours.Add(drawn);
            // Ряда без наложения нет — размера нет: тесное окно, тело видно и по масштабу.
            this.DimensionRow(graphics, slot, placed, true, drawn.Left, drawn.Right, drawn.Bottom, Fmt(frame.UMax - frame.UMin) + " " + unit);
            this.DimensionRow(graphics, slot, placed, false, drawn.Top, drawn.Bottom, drawn.Left, Fmt(frame.VMax - frame.VMin) + " " + unit);
        }

        /// <summary>Рамка свободна: нет меток, выносок, подписей выносок и линий уже стоящих размеров.</summary>
        /// <remarks><paramref name="room"/> — просвет до выносок (линии и подписи) и до меток; у числа размера — <see cref="CalloutMarkRoom"/>.</remarks>
        bool NumberFree(RectangleF box, ViewSlot slot, List<PlacedCallout> placed, float room = 0f)
        {
            if (!RectangleF.Inflate(slot.Panel, -1, -1).Contains(box) || LeaderHits(placed, box, Math.Max(1f, room)))
            {
                return false;
            }

            // (`AMBER324`) Число со стороны тела не садится на тело (п. 5.31): прежде число стояло только снаружи, проверки не было.
            foreach (RectangleF contour in this.paintContours)
            {
                if (RectangleF.Intersect(contour, box).Width > 0f && RectangleF.Intersect(contour, box).Height > 0f)
                {
                    return false;
                }
            }

            RectangleF wide = RectangleF.Inflate(box, room, room);
            foreach (PlacedCallout item in placed)
            {
                if (RectangleF.Intersect(item.Box, wide).Width > 0f && RectangleF.Intersect(item.Box, wide).Height > 0f)
                {
                    return false;
                }
            }

            foreach (SketchMark mark in this.paintMarks)
            {
                if (RectangleF.Intersect(mark.Bounds, wide).Width > 0f && RectangleF.Intersect(mark.Bounds, wide).Height > 0f)
                {
                    return false;
                }
            }

            // Масштабная черта: число и его продолжение в неё не упираются.
            foreach (KeyValuePair<RectangleF, double> bar in this.paintScales)
            {
                if (RectangleF.Intersect(bar.Key, box).Width > 0f && RectangleF.Intersect(bar.Key, box).Height > 0f)
                {
                    return false;
                }
            }

            foreach (GeometrySketch.DimLine line in this.paintDims)
            {
                RectangleF extent = RectangleF.FromLTRB(Math.Min(line.A.X, line.B.X), Math.Min(line.A.Y, line.B.Y), Math.Max(line.A.X, line.B.X) + 0.5f, Math.Max(line.A.Y, line.B.Y) + 0.5f);
                if (RectangleF.Intersect(extent, box).Width > 0f && RectangleF.Intersect(extent, box).Height > 0f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Линия не идёт через подпись и метку; вид сверху у выносок проверяется в `NumberFree`.</summary>
        bool LineFree(PointF p, PointF q, List<PlacedCallout> placed)
        {
            RectangleF extent = RectangleF.FromLTRB(Math.Min(p.X, q.X) - 0.5f, Math.Min(p.Y, q.Y) - 0.5f, Math.Max(p.X, q.X) + 0.5f, Math.Max(p.Y, q.Y) + 0.5f);
            foreach (SketchMark mark in this.paintMarks)
            {
                if (RectangleF.Intersect(mark.Bounds, extent).Width > 0f && RectangleF.Intersect(mark.Bounds, extent).Height > 0f)
                {
                    return false;
                }
            }

            foreach (PlacedCallout item in placed)
            {
                if (RectangleF.Intersect(item.Box, extent).Width > 0f && RectangleF.Intersect(item.Box, extent).Height > 0f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Один размер: a, b вдоль оси, from — край тела, откуда идут выносные. Ряд — ближний, где не идёт через метки и подписи;
        /// шаг тот же, что у рядов редактора. Число над линией у середины (п. 5.24), иначе на продолжении (п. 5.21, 5.30).
        /// </summary>
        void DimensionRow(Graphics graphics, ViewSlot slot, List<PlacedCallout> placed, bool horizontal, float a, float b, float from, string text)
        {
            const float TailPx = 8f, ReachPx = 5f, ArrowRoomPx = 12f;
            float dir = horizontal ? 1f : -1f;
            SizeF size = graphics.MeasureString(text, this.Font);
            // (`AMBER324`) Число всегда горизонтально: вдоль вертикальной линии оно занимает свою высоту.
            float along = horizontal ? size.Width : size.Height;
            float span = b - a, center = 0.5f * (a + b);
            bool arrowsInside = span >= ArrowRoomPx;
            float tail = arrowsInside ? 0f : TailPx;
            Func<float, float, PointF> at2 = (u, v) => horizontal ? new PointF(u, v) : new PointF(v, u);
            for (int row = 0; row < 3; row++)
            {
                float line = from + dir * (GeometrySketch.DimFirstPx + row * GeometrySketch.DimPitchPx);
                float tip = line + dir * GeometrySketch.DimOverPx;
                if (!this.LineFree(at2(a - tail, line), at2(b + tail, line), placed) || !this.LineFree(at2(a, from), at2(a, tip), placed) || !this.LineFree(at2(b, from), at2(b, tip), placed))
                {
                    continue;
                }

                // Над линией: от середины в обе стороны. (`AMBER324`) У вертикальной — рядом с линией, сперва снаружи,
                // потом со стороны тела (`inner`).
                float at = center, lineFrom = a - tail, lineTo = b + tail;
                bool found = false, inner = false;
                bool[] sides = horizontal ? new[] { false } : new[] { false, true };
                float reach = 0.5f * span - 4f - along / 2f;
                for (int k = 0; k < 200 && !found && reach >= 0f; k++)
                {
                    float here = center + (k % 2 == 0 ? 1f : -1f) * ((k + 1) / 2) * 2f;
                    foreach (bool side in sides)
                    {
                        if (!found && Math.Abs(here - center) <= reach && this.NumberFree(NumberBox(horizontal, line, here, size, side), slot, placed, CalloutMarkRoom))
                        {
                            at = here;
                            inner = side;
                            found = true;
                        }
                    }
                }

                // На продолжении. Вертикальный сперва вверх: внизу его ждёт ряд ширины.
                float afterAt = b + tail + ReachPx + along / 2f, beforeAt = a - tail - ReachPx - along / 2f;
                foreach (bool after in horizontal ? new[] { true, false } : new[] { false, true })
                {
                    float there = after ? afterAt : beforeAt;
                    foreach (bool side in sides)
                    {
                    if (!found && this.NumberFree(NumberBox(horizontal, line, there, size, side), slot, placed, CalloutMarkRoom)
                        && this.LineFree(at2(after ? b : there - along / 2f, line), at2(after ? there + along / 2f : a, line), placed))
                    {
                        at = there;
                        inner = side;
                        if (after)
                        {
                            lineTo = there + along / 2f;
                        }
                        else
                        {
                            lineFrom = there - along / 2f;
                        }

                        found = true;
                    }
                    }
                }

                if (!found)
                {
                    continue;
                }

                using (Pen pen = new Pen(Ink, 1f))
                using (Pen ext = new Pen(GeometrySketch.DimGuideInk, GeometrySketch.DimGuideWidth))
                {
                    int group = this.paintDims.Count;
                    GeometrySketch.DimSide side = horizontal ? GeometrySketch.DimSide.Bottom : GeometrySketch.DimSide.Left;
                    Action<GeometrySketch.DimLineKind, PointF, PointF> add = (kind, p, q) =>
                    {
                        graphics.DrawLine(kind == GeometrySketch.DimLineKind.Extension ? ext : pen, p, q);
                        this.paintDims.Add(new GeometrySketch.DimLine { A = p, B = q, Kind = kind, Side = side, Row = row, Group = group, Key = text, SpanA = a, SpanB = b });
                    };
                    add(GeometrySketch.DimLineKind.Dimension, at2(lineFrom, line), at2(lineTo, line));
                    add(GeometrySketch.DimLineKind.Extension, at2(a, from), at2(a, tip));
                    add(GeometrySketch.DimLineKind.Extension, at2(b, from), at2(b, tip));
                    GeometrySketch.Arrow(graphics, pen, horizontal, a, line, arrowsInside ? 1f : -1f);
                    GeometrySketch.Arrow(graphics, pen, horizontal, b, line, arrowsInside ? -1f : 1f);
                }

                RectangleF box = NumberBox(horizontal, line, at, size, inner);
                this.Mark(graphics, text, this.Font, InkBrush, box.X, box.Y, size);

                return;
            }
        }

        /// <summary>
        /// Рамка числа над линией; у вертикальной — рядом с ней слева (`inner` — справа, со стороны тела), НЕ повёрнутое (`AMBER324`, решение Amber 08.11.2026
        /// вопросником, дословно: «Все числа горизонтально (Рекомендую)»; разбор — в шапке простановки `GeometrySketch`).
        /// </summary>
        static RectangleF NumberBox(bool horizontal, float line, float along, SizeF size, bool inner)
        {
            return horizontal ? new RectangleF(along - size.Width / 2f, line - size.Height - 1f, size.Width, size.Height)
                              : new RectangleF(inner ? line + 4f : line - 4f - size.Width, along - size.Height / 2f, size.Width, size.Height);
        }

        /// <summary>Подпись вида: каких тел на нём нет и почему. Без неё неполный чертёж выглядел бы полным.</summary>
        void DrawViewNotes(Graphics graphics, ViewSlot slot, int index, float top)
        {
            List<string> notes = new List<string>();
            int normal = slot.Normal;

            // Правило плоскости названо словами: разрез идёт по нулю, а если ноль вне габарита — по середине.
            if (slot.Axial && slot.Plane != 0.0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchPlaneMiddle", "The plane {0} = 0 misses the detector: the section goes through the middle of its extent"),
                    AxisLetter(normal)));
            }

            if (this.offAxis.Count > 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchChord", "The plane misses the axis, the width here is a chord, not the diameter ({0}): {1}"),
                    this.offAxis.Count, NameList(this.offAxis)));
            }

            if (this.unions.Count > 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchUnion", "Union, drawn as one body with a common outline ({0}): {1}"),
                    this.unions.Count, NameList(this.unions)));
            }

            if (this.tooComplex.Count > 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchTooComplex", "Not drawn, too complex for this drawing ({0}): {1}"),
                    this.tooComplex.Count, NameList(this.tooComplex)));
            }

            // (`AMBER301`, fixGdml2) Две причины — две подписи, числами деталей, как в сводке: «Неподдержанных тел: N,
            // положение или габарит не прочитаны: M». Прежде одна подпись на обе причины и «0» в сводке говорили разное.
            if (this.notDrawn.Count > 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchNotDrawn", "Not drawn ({0}): {1} - unsupported body"),
                    this.notDrawn.Count, NameList(this.notDrawn)));
            }

            if (this.unplaced.Count > 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchNotPlaced", "Not drawn ({0}): {1} - placement or extent not read"),
                    this.unplaced.Count, NameList(this.unplaced)));
            }

            if (this.noSection.Count > 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchNoSection", "No exact section in this view ({0}): {1}"),
                    this.noSection.Count, NameList(this.noSection)));
            }

            if (this.trimmed.Count > 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchTrimmed", "Not shown, thousands of times larger than the rest ({0}): {1}"),
                    this.trimmed.Count, NameList(this.trimmed)));
            }

            if (this.contentHidden.Count > 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchContentHidden", "Content not shown ({0}): {1} - not read: {2}"),
                    this.contentHidden.Count, NameList(this.contentHidden), string.Join(", ", System.Linq.Enumerable.Take(this.contentReasons, 3)) + (this.contentReasons.Count > 3 ? ", …" : "")));
            }

            if (this.envelopes.Count > 0)
            {
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchEnvelope", "Not shown, an envelope around the whole scene ({0}): {1}; the frame is set by what it holds"),
                    this.envelopes.Count, NameList(this.envelopes)));
            }

            if (this.linesOnly.Count > 0)
            {
                // Причин три: поверхность не замкнута, вывернута, ветвится; какая именно — в сводке.
                notes.Add(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchMeshOutline", "Outline only, no fill: the mesh does not define what is inside, see the summary ({0}): {1}"),
                    this.linesOnly.Count, NameList(this.linesOnly)));
            }

            if (this.notDrawn.Count == 0 && this.unplaced.Count == 0 && this.noSection.Count == 0 && this.tooComplex.Count == 0 && this.shapesDrawn == 0)
            {
                notes.Add(GdmlText.T("GdmlSketchEmptySection", "This plane crosses no body"));
            }

            this.paintNotes.AddRange(notes);
            if (!slot.Text)
            {
                return;
            }

            // Подписи занимают не больше двух пятых вида: чертёж остаётся чертежом, полный список — в сводке.
            float limit = slot.Panel.Height * 0.4f;
            float wrap = Math.Max(10f, slot.Panel.Width - 22f);
            float used = 0f;
            float y = top;
            foreach (string note in notes)
            {
                SizeF size = graphics.MeasureString(note, this.Font, (int)wrap);
                if (used + size.Height > limit)
                {
                    break;
                }

                graphics.FillRectangle(NoteBrush, slot.Panel.Left + 8f, y + 2f, 2f, size.Height - 4f);
                graphics.DrawString(note, this.Font, InkBrush, new RectangleF(slot.Panel.Left + 13f, y, wrap, size.Height));
                this.paintMarks.Add(new SketchMark { Bounds = new RectangleF(slot.Panel.Left + 13f, y, size.Width, size.Height), Text = note });
                y += size.Height;
                used += size.Height;
            }

            this.neededNotes[index] = used;
        }

        // ------------------------------------------------------------------
        // Выноски
        // ------------------------------------------------------------------

        readonly Dictionary<string, List<KeyValuePair<CalloutText, PointF>>[]> calloutPlaces = new Dictionary<string, List<KeyValuePair<CalloutText, PointF>>[]>();

        struct CalloutText
        {
            public GdmlRole Role;
            public GdmlVolumeInstance Volume;
            public string Head, Detail;
        }

        /// <summary>
        /// (`AMBER301`, дизайнер 11.11.2026) Выноски равны ролям блока «Сопоставление»: кристалл, проба, защита — только
        /// взятые в расчёт, плюс сборка детектора («Детектор», решение Amber 11.11.2026 вопросником, дословно:
        /// ««Детектор» (Рекомендую)»), если её размеры не повторяют габарит на размерных линиях. Сосуд с чертежа ушёл:
        /// его нет в «Сопоставлении», он остаётся в легенде. Бледная деталь выноски не получает. На поперечном разрезе
        /// одна выноска — кристалл: остальное подписано на виде сбоку.
        /// </summary>
        List<CalloutText> CalloutTexts(bool shortLabels, bool axial)
        {
            List<CalloutText> items = new List<CalloutText>();
            GdmlOverview o = this.Overview;
            GdmlSketchMarks m = this.marks;
            Action<GdmlRole, GdmlVolumeInstance, string> add = (role, volume, detail) =>
            {
                if (volume != null && (this.pale == null || !this.pale(volume)))
                {
                    items.Add(new CalloutText
                    {
                        Role = role,
                        Volume = volume,
                        Head = GdmlOverview.RoleName(role),
                        Detail = shortLabels ? null : detail,
                    });
                }
            };
            GdmlVolumeInstance crystal = m != null ? m.Crystal : o.Crystal;
            GdmlVolumeInstance sample = m != null ? m.Sample : o.Sample;
            add(GdmlRole.Crystal, crystal, crystal == null ? null : GdmlOverview.SizeText(crystal, this.geometry.Axis));
            if (!axial)
            {
                return items;
            }

            add(GdmlRole.Sample, sample, sample == null ? null : GdmlOverview.SizeText(sample, this.geometry.Axis));
            add(GdmlRole.Shield, m == null ? null : m.Shield, null);
            if (o.DeviceBody != null && !ReferenceEquals(o.DeviceBody, crystal) && !o.DeviceFillsScene())
            {
                add(GdmlRole.Device, o.DeviceBody, o.DeviceSizeText());
            }

            return items;
        }

        void MeasureCallouts(Graphics graphics, bool shortLabels, out float width, out float height)
        {
            width = 0f;
            height = 0f;
            foreach (CalloutText item in this.CalloutTexts(shortLabels, true))
            {
                SizeF head = graphics.MeasureString(item.Head, this.LabelFont);
                SizeF detail = item.Detail == null ? SizeF.Empty : graphics.MeasureString(item.Detail, this.Font);
                width = Math.Max(width, Math.Max(head.Width, detail.Width));
                height = Math.Max(height, head.Height + detail.Height);
            }
        }

        /// <summary>
        /// Точки внутри сечения тома на этом виде: сетка по полю чертежа, годятся те, вокруг которых
        /// на 4 точки есть само тело. Середина габарита не годится: у маринелли там колодец, а не проба.
        /// Тело тоньше этого — точки вдоль самых широких полос области. Пусто — на этом виде тома нет.
        /// </summary>
        List<PointF> AnchorsOf(ViewContext view, GdmlVolumeInstance volume, Rectangle plot, RectangleF drawn)
        {
            List<PointF> points = new List<PointF>();
            if (volume.Solid == null || volume.Transform == null || !volume.BoundsKnown || !volume.PlacementKnown
                || !IntersectsPlane(volume, view.Normal, view.Plane))
            {
                return points;
            }

            using (Region region = this.AreaOf(view, volume))
            using (Matrix identity = new Matrix())
            {
                if (region == null)
                {
                    return points;
                }

                int children = 0;
                foreach (GdmlVolumeInstance child in this.geometry.Volumes)
                {
                    if (child == null || child.Parent != volume || !child.BoundsKnown || !IntersectsPlane(child, view.Normal, view.Plane))
                    {
                        continue;
                    }

                    if (++children > 64)
                    {
                        break;
                    }

                    using (Region inner = this.AreaOf(view, child))
                    {
                        if (inner != null)
                        {
                            region.Exclude(inner);
                        }
                    }
                }

                // (`AMBER301`) Точка выноски не ставится на бледную деталь: на RC-110 «Прибор» упирался в аккумулятор,
                // и человек читал «прибор = розовый прямоугольник». Бледные детали внутри тела вырезаются, как дети.
                if (this.pale != null)
                {
                    // Предки не в счёт: сечение бледного тома-вместилища (воздух внутри корпуса) покрывает и само тело.
                    HashSet<GdmlVolumeInstance> above = new HashSet<GdmlVolumeInstance>();
                    for (GdmlVolumeInstance up = volume.Parent; up != null && above.Add(up); up = up.Parent)
                    {
                    }

                    int faded = 0;
                    foreach (GdmlVolumeInstance other in this.geometry.Volumes)
                    {
                        if (other == null || ReferenceEquals(other, volume) || other.Parent == volume || above.Contains(other) || !other.BoundsKnown
                            || !IntersectsPlane(other, view.Normal, view.Plane) || !this.pale(other))
                        {
                            continue;
                        }

                        if (++faded > 64)
                        {
                            break;
                        }

                        using (Region inner = this.AreaOf(view, other))
                        {
                            if (inner != null)
                            {
                                region.Exclude(inner);
                            }
                        }
                    }
                }

                region.Intersect(plot);
                const float Deep = 4f;
                float step = Math.Max(6f, Math.Max(drawn.Width, drawn.Height) / 40f);
                for (float y = drawn.Top + step / 2f; y < drawn.Bottom; y += step)
                {
                    for (float x = drawn.Left + step / 2f; x < drawn.Right; x += step)
                    {
                        if (region.IsVisible(x, y) && region.IsVisible(x - Deep, y) && region.IsVisible(x + Deep, y)
                            && region.IsVisible(x, y - Deep) && region.IsVisible(x, y + Deep))
                        {
                            points.Add(new PointF(x, y));
                        }
                    }
                }

                // Тонкая стенка: глубоких точек нет. Тогда — вдоль широких полос самой области,
                // чтобы и здесь было из чего выбрать точку в стороне от оси.
                if (points.Count == 0)
                {
                    RectangleF[] scans = region.GetRegionScans(identity);
                    float best = 0f;
                    foreach (RectangleF scan in scans)
                    {
                        best = Math.Max(best, scan.Width * scan.Height);
                    }

                    foreach (RectangleF scan in scans)
                    {
                        if (scan.Width * scan.Height < best / 2f)
                        {
                            continue;
                        }

                        for (int i = 1; i < 8; i++)
                        {
                            float at = i / 8f;
                            points.Add(new PointF(scan.Left + scan.Width * (scan.Width > scan.Height ? at : 0.5f),
                                                  scan.Top + scan.Height * (scan.Width > scan.Height ? 0.5f : at)));
                        }
                    }
                }

                return points;
            }
        }

        Region AreaOf(ViewContext view, GdmlVolumeInstance volume)
        {
            GdmlSolid solid = volume.Solid;
            if (solid == null || solid.Kind == GdmlSolidKind.Unknown || volume.Transform == null || (IsBoolean(solid) && solid.First == null))
            {
                return null;
            }

            if (IsBoolean(solid))
            {
                ShapeCost cost = this.CostOf(solid);
                if (cost.Rejected || cost.Depth > MaxShapeDepth || cost.Parts > MaxShapeParts || cost.Vertices > MaxViewVertices)
                {
                    return null;
                }
            }

            if (solid.Kind == GdmlSolidKind.Tessellated)
            {
                using (GraphicsPath area = this.MeshSection(view, solid, volume.Transform))
                {
                    return area == null ? null : new Region(area);
                }
            }

            return this.CollectedOf(view, solid, volume.Transform).Area.Clone();
        }

        /// <summary>Выноска на своём месте: всё, что нужно нарисовать её и обойти её линию.</summary>
        sealed class PlacedCallout
        {
            public CalloutText Text;
            public PointF Anchor, From;
            public RectangleF Box;
            public bool Brief;
        }

        /// <summary>Сколько раскладок выносок помнить: при протяжке окна каждый размер — новая раскладка.</summary>
        const int MaxCalloutPlaces = 16;

        /// <summary>
        /// Выноски вида. Точка берётся у края тела, ближнего к полю подписей, и на той стороне, где подписей
        /// меньше: выноска короче, и подписи не сбиваются на одну сторону. На строку оси точка не ложится,
        /// и линия не идёт через чужую точку.
        /// </summary>
        List<PlacedCallout> PlanCallouts(Graphics graphics, ViewFrame frame, ViewSlot slot)
        {
            List<PlacedCallout> result = new List<PlacedCallout>();
            List<CalloutText> texts = this.CalloutTexts(slot.ShortLabels, slot.Axial);
            if (texts.Count == 0)
            {
                return result;
            }

            RectangleF drawn = frame.Drawn;
            bool columns = slot.Mode == CalloutMode.Columns;
            float axisRow = slot.Axial && frame.VMin <= 0.0 && frame.VMax >= 0.0 ? frame.ToScreen(frame.UMin, 0.0).Y : float.NaN;
            float cutColumn = slot.Axial && IsFinite(this.geometry.EndPlane) ? frame.ToScreen(this.geometry.EndPlane, 0.0).X : float.NaN;

            // Точки считаются один раз на раскладку: перерисовка без смены размера их не трогает.
            string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}|{6}", slot.Axial, slot.Plot, frame.Scale, drawn.Left, drawn.Top, slot.Mode, slot.ShortLabels);
            List<KeyValuePair<CalloutText, PointF>>[] known;
            if (!this.calloutPlaces.TryGetValue(key, out known))
            {
                known = this.ChooseAnchors(texts, frame, slot, drawn, columns, axisRow, cutColumn);
                if (this.calloutPlaces.Count >= MaxCalloutPlaces)
                {
                    this.calloutPlaces.Clear();
                }

                this.calloutPlaces[key] = known;
            }

            // Где встанет буква «A» линии разреза (то же условие, что в `DrawGuides`): ряд подписей над чертежом её обходит.
            float letterColumn = slot.Axial && slot.Text && IsFinite(this.geometry.EndPlane) && this.geometry.EndPlane >= frame.UMin && this.geometry.EndPlane <= frame.UMax
                ? cutColumn
                : float.NaN;
            this.PlaceSide(graphics, slot, drawn, known[0], true, result, letterColumn);
            this.PlaceSide(graphics, slot, drawn, known[1], false, result, letterColumn);
            return result;
        }

        List<KeyValuePair<CalloutText, PointF>>[] ChooseAnchors(List<CalloutText> texts, ViewFrame frame, ViewSlot slot, RectangleF drawn,
                                                                bool columns, float axisRow, float cutColumn)
        {
            List<KeyValuePair<CalloutText, PointF>> first = new List<KeyValuePair<CalloutText, PointF>>();
            List<KeyValuePair<CalloutText, PointF>> second = new List<KeyValuePair<CalloutText, PointF>>();
            ViewContext view = new ViewContext
            {
                Frame = frame,
                Normal = slot.Normal,
                U = slot.U,
                V = slot.V,
                Plane = slot.Plane,
            };
            try
            {
                foreach (CalloutText item in texts)
                {
                    view.Volume = item.Volume;
                    float bestScore = float.MaxValue;
                    PointF best = PointF.Empty;
                    bool bestFirst = true;
                    foreach (PointF point in this.AnchorsOf(view, item.Volume, slot.Plot, drawn))
                    {
                        // На оси и на линии A–A точка теряется: туда только если больше некуда.
                        float onGuide = Math.Abs(point.Y - axisRow) < 4f || Math.Abs(point.X - cutColumn) < 4f ? 1e4f : 0f;
                        float toFirst = columns ? point.X - drawn.Left : point.Y - drawn.Top;
                        float toSecond = columns ? drawn.Right - point.X : drawn.Bottom - point.Y;
                        foreach (bool side in new[] { true, false })
                        {
                            // Подпись на этой стороне уже есть: сюда дороже. Цена — четверть чертежа.
                            float crowd = (side ? first.Count : second.Count) * (columns ? drawn.Width : drawn.Height) * 0.25f;

                            // Меньшая добавка: ближе к середине стороны, а не в углу.
                            float along = columns ? Math.Abs(point.Y - (drawn.Top + drawn.Height / 2f)) : Math.Abs(point.X - (drawn.Left + drawn.Width / 2f));
                            float crossing = CrossesDot(point, side, columns, drawn, first, true) || CrossesDot(point, side, columns, drawn, second, false) ? 1e3f : 0f;
                            float score = (side ? toFirst : toSecond) + crowd + onGuide + crossing + 0.15f * along;
                            if (score < bestScore)
                            {
                                bestScore = score;
                                best = point;
                                bestFirst = side;
                            }
                        }
                    }

                    if (bestScore < float.MaxValue)
                    {
                        (bestFirst ? first : second).Add(new KeyValuePair<CalloutText, PointF>(item, best));
                    }
                }
            }
            finally
            {
                view.ReleaseCache();
            }

            return new[] { first, second };
        }

        /// <summary>
        /// Пройдёт ли линия новой точки через уже выбранную точку или линия выбранной через новую. Линия берётся
        /// по кратчайшему пути к полю подписей: куда сдвинет подпись раздвижка, на этом шаге ещё не известно.
        /// </summary>
        static bool CrossesDot(PointF point, bool side, bool columns, RectangleF drawn, List<KeyValuePair<CalloutText, PointF>> placed, bool placedFirst)
        {
            const float Near = 7f;
            float pointAcross = columns ? point.Y : point.X;
            float pointAlong = columns ? point.X : point.Y;
            float pointEdge = side ? (columns ? drawn.Left : drawn.Top) : (columns ? drawn.Right : drawn.Bottom);
            foreach (KeyValuePair<CalloutText, PointF> other in placed)
            {
                float otherAcross = columns ? other.Value.Y : other.Value.X;
                float otherAlong = columns ? other.Value.X : other.Value.Y;
                if (Math.Abs(otherAcross - pointAcross) >= Near)
                {
                    continue;
                }

                float otherEdge = placedFirst ? (columns ? drawn.Left : drawn.Top) : (columns ? drawn.Right : drawn.Bottom);
                if ((otherAlong >= Math.Min(pointAlong, pointEdge) && otherAlong <= Math.Max(pointAlong, pointEdge))
                    || (pointAlong >= Math.Min(otherAlong, otherEdge) && pointAlong <= Math.Max(otherAlong, otherEdge)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Подписи одной стороны: по порядку вдоль стороны, без наложения. Порядок по положению точек — чтобы
        /// выноски не перекрещивались. Подписи стоят вплотную к чертежу, а не у края окна. Не помещаются с
        /// размерами — остаются только названия.
        /// </summary>
        void PlaceSide(Graphics graphics, ViewSlot slot, RectangleF drawn, List<KeyValuePair<CalloutText, PointF>> items, bool first, List<PlacedCallout> result, float letterColumn)
        {
            if (items.Count == 0)
            {
                return;
            }

            bool columns = slot.Mode == CalloutMode.Columns;
            items.Sort((a, b) => columns ? a.Value.Y.CompareTo(b.Value.Y) : a.Value.X.CompareTo(b.Value.X));
            Rectangle panel = slot.Panel;
            Rectangle plot = slot.Plot;
            // (`AMBER301`, критик 12.11.2026) В ряд подписи стоят реже: «Защита» и «Проба» в 6 точках друг от друга читались
            // одной строкой «Защита Проба». В столбце просвет прежний: строки и так раздельны.
            float Gap = columns ? 6f : 14f;
            float low = columns ? plot.Top : panel.Left + 6f;
            float high = columns ? plot.Bottom : panel.Right - 6f;

            // (`a301c`) Со стороны, где стоит размер габарита (под чертежом и слева), подпись — за его первым рядом, с просветом:
            // прежде она стояла в 5 точках от вылета выносных линий и читалась одной строкой с числом размера.
            bool dimensions = slot.Text && !this.compact;

            bool brief = false;
            float length = 0f;
            foreach (KeyValuePair<CalloutText, PointF> item in items)
            {
                SizeF size = this.CalloutSize(graphics, item.Key, false);
                length += (columns ? size.Height : size.Width) + Gap;
            }

            brief = length - Gap > high - low;

            List<RectangleF> boxes = new List<RectangleF>();
            foreach (KeyValuePair<CalloutText, PointF> item in items)
            {
                SizeF size = this.CalloutSize(graphics, item.Key, brief);
                float x, y;
                if (columns)
                {
                    x = first
                        ? Math.Max(panel.Left + 6f, drawn.Left - (dimensions ? CalloutBeyondDims : 30f) - size.Width)
                        : Math.Min(panel.Right - 6f - size.Width, drawn.Right + 22f);
                    y = item.Value.Y - size.Height / 2f;
                }
                else
                {
                    x = item.Value.X - size.Width / 2f;
                    y = first ? drawn.Top - 16f - size.Height : drawn.Bottom + (dimensions ? CalloutBeyondDims : 34f);
                }

                boxes.Add(new RectangleF(x, y, size.Width, size.Height));
            }

            // Раздвинуть вперёд, затем при вылете за край — назад.
            for (int i = 0; i < boxes.Count; i++)
            {
                RectangleF box = boxes[i];
                float minimum = i == 0 ? low : (columns ? boxes[i - 1].Bottom : boxes[i - 1].Right) + Gap;
                if (columns)
                {
                    box.Y = Math.Max(box.Y, minimum);
                }
                else
                {
                    box.X = Math.Max(box.X, minimum);
                }

                boxes[i] = box;
            }

            for (int i = boxes.Count - 1; i >= 0; i--)
            {
                RectangleF box = boxes[i];
                float maximum = i == boxes.Count - 1 ? high : (columns ? boxes[i + 1].Top : boxes[i + 1].Left) - Gap;
                if (columns)
                {
                    box.Y = Math.Min(box.Y, maximum - box.Height);
                }
                else
                {
                    box.X = Math.Min(box.X, maximum - box.Width);
                }

                boxes[i] = box;
            }

            // (`a301c`) Ряд над чертежом и буква «A» линии разреза: подпись над буквой (с любой стороны линии) поднимается
            // выше неё на просвет — весь ряд разом, чтобы подписи остались в одну строку. Прежде подпись стояла вплотную к букве.
            if (!columns && first && !float.IsNaN(letterColumn))
            {
                SizeF letter = graphics.MeasureString("A", this.LabelFont);
                float reach = 2f + letter.Width + CalloutMarkRoom;
                float lift = letter.Height + 1f + CalloutMarkRoom + 1f - 16f;
                if (lift > 0f && boxes.Exists(box => box.Left < letterColumn + reach && box.Right > letterColumn - reach))
                {
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        RectangleF box = boxes[i];
                        box.Y -= lift;
                        boxes[i] = box;
                    }
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                RectangleF box = boxes[i];
                PointF anchor = items[i].Value;
                result.Add(new PlacedCallout
                {
                    Text = items[i].Key,
                    Anchor = anchor,
                    Box = box,
                    Brief = brief,
                    From = columns
                        ? new PointF(first ? box.Right + 2f : box.Left - 2f, box.Top + box.Height / 2f)
                        : new PointF(Math.Max(box.Left, Math.Min(box.Right, anchor.X)), first ? box.Bottom + 1f : box.Top - 1f),
                });
            }
        }

        void RenderCallouts(Graphics graphics, ViewSlot slot, List<PlacedCallout> placed)
        {
            using (Pen leader = new Pen(Ink, 0.9f))
            using (Brush halo = new SolidBrush(Color.White))
            {
                foreach (PlacedCallout item in placed)
                {
                    graphics.DrawLine(leader, item.From, item.Anchor);
                }

                // Точки — поверх всех линий: чужая линия, если уж прошла рядом, точку не закрывает.
                foreach (PlacedCallout item in placed)
                {
                    PointF anchor = item.Anchor;
                    graphics.FillEllipse(halo, anchor.X - 3f, anchor.Y - 3f, 6f, 6f);
                    graphics.FillEllipse(InkBrush, anchor.X - 2f, anchor.Y - 2f, 4f, 4f);

                    SizeF head = graphics.MeasureString(item.Text.Head, this.LabelFont);
                    this.Mark(graphics, item.Text.Head, this.LabelFont, InkBrush, item.Box.Left, item.Box.Top, head);
                    if (item.Text.Detail != null && !item.Brief)
                    {
                        SizeF detail = graphics.MeasureString(item.Text.Detail, this.Font);
                        this.Mark(graphics, item.Text.Detail, this.Font, InkBrush, item.Box.Left, item.Box.Top + head.Height, detail);
                    }

                    this.paintCallouts.Add(new SketchCallout
                    {
                        Role = item.Text.Role,
                        MaterialName = item.Text.Volume.MaterialName,
                        Axial = slot.Axial,
                        Anchor = anchor,
                        From = item.From,
                        Label = item.Box,
                    });
                }
            }
        }

        SizeF CalloutSize(Graphics graphics, CalloutText item, bool brief)
        {
            SizeF head = graphics.MeasureString(item.Head, this.LabelFont);
            SizeF detail = item.Detail == null || brief ? SizeF.Empty : graphics.MeasureString(item.Detail, this.Font);
            return new SizeF(Math.Max(head.Width, detail.Width), head.Height + detail.Height);
        }

        // ------------------------------------------------------------------
        // Легенда веществ
        // ------------------------------------------------------------------

        sealed class LegendCell
        {
            public string Text;
            public Color? Swatch;
            public float X, Y, Width, Height;
            public bool Bold;

            /// <summary>Вещество не вошло в расчёт: значок бледный, как деталь на чертеже, в тонкой серой рамке.</summary>
            public bool Faded;

            /// <summary>Заголовок второй группы: с новой строки.</summary>
            public bool NewLine;
        }

        const float SwatchSize = 10f;

        /// <summary>Рамка бледного значка легенды: значок виден на холсте и не путается с газом.</summary>
        static readonly Color FadedEdge = Color.FromArgb(0x8A, 0x92, 0x9C);

        /// <summary>
        /// Строки легенды по порядку. (`AMBER301`, дизайнер 11.11.2026) Когда известно, что вошло в расчёт (<see cref="SetPale"/>),
        /// групп две: «В расчёте:» — по ролям (кристалл, отражатель, корпус, проба, сосуд, защита, прочее по массе) и
        /// «Не вошло в расчёт:» — с бледными значками: бледность на чертеже получает имя прямо под ним. Роль стоит перед
        /// именем вещества: человеческое слово не прячется за машинным. Вещество, занятое хотя бы одной деталью расчёта, —
        /// в первой группе. Без раскладки группа одна, «Вещества:», тяжёлые первыми.
        /// </summary>
        List<LegendCell> LegendEntries()
        {
            GdmlOverview o = this.Overview;
            GdmlSketchMarks m = this.marks;
            List<LegendCell> entries = new List<LegendCell>();
            HashSet<string> used = null;
            if (this.pale != null)
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
                {
                    if (volume != null && !this.pale(volume))
                    {
                        used.Add(volume.MaterialName ?? "");
                    }
                }
            }

            string crystalMaterial = m == null || m.Crystal == null ? null : m.Crystal.MaterialName ?? "";
            string sampleMaterial = m == null || m.Sample == null ? null : m.Sample.MaterialName ?? "";
            Func<GdmlMaterialRow, GdmlRole> roleOf = row =>
            {
                // Роль вещества — по выбору человека в «Сопоставлении», если он есть: иначе «Кристалл:» остался бы у вещества,
                // которое программа узнала сама, а человек поправил.
                if (crystalMaterial != null && row.Name == crystalMaterial)
                {
                    return GdmlRole.Crystal;
                }

                if (sampleMaterial != null && row.Name == sampleMaterial)
                {
                    return GdmlRole.Sample;
                }

                if (m != null && m.ShieldMaterials.Contains(row.Name))
                {
                    return GdmlRole.Shield;
                }

                if ((row.Role == GdmlRole.Crystal && crystalMaterial != null) || (row.Role == GdmlRole.Sample && m != null))
                {
                    return GdmlRole.None;
                }

                return row.Role;
            };
            GdmlRole[] order = { GdmlRole.Crystal, GdmlRole.Reflector, GdmlRole.Housing, GdmlRole.Sample, GdmlRole.Vessel, GdmlRole.Shield };
            Func<GdmlMaterialRow, int> rank = row =>
            {
                int at = Array.IndexOf(order, roleOf(row));
                return at < 0 ? order.Length : at;
            };

            List<GdmlMaterialRow> first = new List<GdmlMaterialRow>(), second = new List<GdmlMaterialRow>();
            foreach (GdmlMaterialRow row in o.Materials)
            {
                // Газ — в расчёте всегда: воздух зазоров модель кладёт сама. Иначе окно импорта (бледное — всё, что не легло в
                // поля) и вкладка (бледное — твёрдое вне полей) ставили бы воздух в разные группы.
                (used == null || used.Contains(row.Name) || (row.Role == GdmlRole.Gas && used.Count > 0) ? first : second).Add(row);
            }

            if (used != null)
            {
                // Устойчиво: внутри роли — прежний порядок, тяжёлые первыми.
                List<KeyValuePair<int, GdmlMaterialRow>> keyed = new List<KeyValuePair<int, GdmlMaterialRow>>();
                for (int i = 0; i < first.Count; i++)
                {
                    keyed.Add(new KeyValuePair<int, GdmlMaterialRow>(rank(first[i]) * 100000 + i, first[i]));
                }

                keyed.Sort((a, b) => a.Key.CompareTo(b.Key));
                first.Clear();
                foreach (KeyValuePair<int, GdmlMaterialRow> item in keyed)
                {
                    first.Add(item.Value);
                }
            }

            Action<GdmlMaterialRow, bool> add = (row, faded) =>
            {
                GdmlRole role = roleOf(row);
                string text = (role == GdmlRole.None ? "" : GdmlOverview.RoleName(role) + ": ") + row.Display
                            + (this.compact ? "" : ", " + row.MassText() + row.UncountedText());
                entries.Add(new LegendCell { Text = text, Swatch = faded ? Fade(row.Color) : row.Color, Faded = faded });
            };
            if (used == null)
            {
                entries.Add(new LegendCell { Text = GdmlText.T("GdmlSketchLegendTitle", "Materials:"), Bold = true });
            }
            else if (first.Count > 0)
            {
                entries.Add(new LegendCell { Text = GdmlText.T("GdmlSketchLegendUsed", "In the calculation:"), Bold = true });
            }

            foreach (GdmlMaterialRow row in first)
            {
                add(row, false);
            }

            if (second.Count > 0)
            {
                entries.Add(new LegendCell { Text = GdmlText.T("GdmlSketchLegendUnused", "Not in the calculation:"), Bold = true, NewLine = entries.Count > 0 });
                foreach (GdmlMaterialRow row in second)
                {
                    add(row, true);
                }
            }

            return entries;
        }

        /// <summary>
        /// Легенда полосой снизу: значок, роль, вещество, масса. Цвет сам по себе ничего не говорил человеку.
        /// Больше четверти высоты не занимает: остальное названо числом и лежит в сводке.
        /// </summary>
        float LayoutLegend(Graphics graphics, out List<LegendCell> cells)
        {
            cells = new List<LegendCell>();
            int width = this.ClientSize.Width, height = this.ClientSize.Height;
            if (width < 240 || height < 180)
            {
                return 0f;
            }

            List<LegendCell> entries = this.LegendEntries();
            float line = Math.Max(graphics.MeasureString("X", this.LabelFont).Height, SwatchSize + 3f);
            float maxHeight = Math.Max(line + 6f, height * (this.Tight ? 0.17f : 0.25f));
            float x = 6f, y = 4f;
            for (int i = 0; i < entries.Count; i++)
            {
                LegendCell entry = entries[i];
                bool title = entry.Swatch == null;
                SizeF size = graphics.MeasureString(entry.Text, title ? this.LabelFont : this.Font);
                float cell = title ? size.Width + 6f : SwatchSize + 3f + size.Width + 10f;
                bool wrap = (entry.NewLine && x > 6f) || (x + cell > width - 4f && x > 6f && cells.Count > 0 && cells[cells.Count - 1].Swatch != null);
                if (wrap)
                {
                    if (y + 2f * line + 4f > maxHeight)
                    {
                        int hidden = 0;
                        for (int k = i; k < entries.Count; k++)
                        {
                            hidden += entries[k].Swatch != null ? 1 : 0;
                        }

                        string more = string.Format(CultureInfo.InvariantCulture,
                            GdmlText.T("GdmlSketchLegendMore", "+{0} more in the summary"), hidden);
                        SizeF moreSize = graphics.MeasureString(more, this.Font);

                        // Не влезает за последней ячейкой: та уступает место, число растёт.
                        while (x + moreSize.Width > width - 4f && cells.Count > 1 && cells[cells.Count - 1].Y == y && cells[cells.Count - 1].Swatch != null)
                        {
                            x = cells[cells.Count - 1].X;
                            cells.RemoveAt(cells.Count - 1);
                            hidden++;
                            more = string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlSketchLegendMore", "+{0} more in the summary"), hidden);
                            moreSize = graphics.MeasureString(more, this.Font);
                        }

                        if (hidden > 0)
                        {
                            cells.Add(new LegendCell { Text = more, X = x, Y = y, Width = moreSize.Width, Height = moreSize.Height });
                        }

                        break;
                    }

                    x = 6f;
                    y += line;
                }

                entry.X = x;
                entry.Y = y;
                entry.Width = title ? size.Width : cell;
                entry.Height = size.Height;
                cells.Add(entry);
                x += cell;
            }

            return y + line + 6f;
        }

        void DrawLegend(Graphics graphics, List<LegendCell> cells, int top, float height)
        {
            if (cells.Count == 0)
            {
                return;
            }

            using (Pen border = new Pen(Divider, 1.0f))
            {
                graphics.DrawLine(border, 0, top, this.ClientSize.Width, top);
            }

            foreach (LegendCell cell in cells)
            {
                float y = top + cell.Y;
                float x = cell.X;
                if (cell.Swatch != null)
                {
                    using (Brush fill = new SolidBrush(cell.Swatch.Value))
                    using (Pen edge = new Pen(cell.Faded ? FadedEdge : Darken(cell.Swatch.Value), 1f))
                    {
                        graphics.FillRectangle(fill, x, y + 1f, SwatchSize, SwatchSize);
                        graphics.DrawRectangle(edge, x, y + 1f, SwatchSize, SwatchSize);
                    }

                    x += SwatchSize + 3f;
                }

                Font font = cell.Bold ? this.LabelFont : this.Font;
                this.Mark(graphics, cell.Text, font, InkBrush, x, y, graphics.MeasureString(cell.Text, font));
            }
        }

        /// <summary>
        /// (`AMBER301`, дизайнер 11.11.2026) Плашка поверх чертежа в верхней трети: файл нарисован, а в расчёт не взят —
        /// без неё чертёж выглядел успешным, а причина стояла мелко в стороне.
        /// </summary>
        void DrawBanner(Graphics graphics, Rectangle area)
        {
            string text = this.marks == null ? null : this.marks.Banner;
            if (string.IsNullOrEmpty(text) || area.Width < 200 || area.Height < 120)
            {
                return;
            }

            this.paintNotes.Add(text);
            int wrap = Math.Min(area.Width - 40, 440);
            SizeF size = graphics.MeasureString(text, this.LabelFont, wrap);
            RectangleF box = new RectangleF(area.Left + (area.Width - size.Width) / 2f - 10f, area.Top + area.Height / 3f - size.Height / 2f - 6f,
                                            size.Width + 20f, size.Height + 12f);
            using (Brush back = new SolidBrush(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)))
            using (Pen edge = new Pen(NoteInk, 1f))
            {
                graphics.FillRectangle(back, box);
                graphics.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);
                graphics.FillRectangle(NoteBrush, box.X, box.Y, 3f, box.Height);
            }

            graphics.DrawString(text, this.LabelFont, InkBrush, new RectangleF(box.X + 10f, box.Y + 6f, size.Width + 1f, size.Height + 1f));
            this.paintBanner = box;
        }

        /// <summary>Рамка плашки последней отрисовки; пусто — плашки нет. Читает проба.</summary>
        RectangleF paintBanner;

        /// <summary>Число: четыре значащие, как у чертежа редактора; от тысячи — целым, точка всегда.</summary>
        internal static string Fmt(double value)
        {
            double size = Math.Abs(value);
            return size >= 1000.0 && size < 1e15
                ? value.ToString("F0", CultureInfo.InvariantCulture)
                : value.ToString("G4", CultureInfo.InvariantCulture);
        }


        /// <summary>
        /// Первые имена и «и ещё N». Имя, размещённое несколько раз, — «vTargetColumn ×3»: счёт идёт деталями, как у сводки, и
        /// хвост «ещё N» — тоже детали, а не имена (`AMBER301`, fixGdml2).
        /// </summary>
        static string NameList(NameSet names)
        {
            List<string> parts = new List<string>();
            int shown = 0;
            foreach (string name in names.Shown)
            {
                int copies = names.CopiesOf(name);
                shown += copies;
                parts.Add(copies > 1 ? name + " ×" + copies.ToString(CultureInfo.InvariantCulture) : name);
            }

            string text = string.Join(", ", parts);
            return names.Count > shown
                ? string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSketchMoreNames", "{0} and {1} more"), text, names.Count - shown)
                : text;
        }

        /// <summary>
        /// Тела подписи вида. Множество, а не список: 30000 томов перебирались в каждой
        /// перерисовке, 17 с. Для подписи хранятся только первые имена: на холсте больше не прочесть.
        ///
        /// (`AMBER301`, fixGdml2) Счёт — ДЕТАЛЯМИ (размещёнными томами), а не именами: сводка считает детали
        /// («Деталей: 23», «неподдержанных тел»), и на `lht.gdml` подпись «(7)» против одиннадцати деталей говорила другое
        /// число. Одна деталь, названная дважды (часть вычитания и само вычитание), считается один раз.
        /// </summary>
        sealed class NameSet
        {
            const int MaxShown = 3;
            readonly HashSet<GdmlVolumeInstance> seen = new HashSet<GdmlVolumeInstance>();
            readonly Dictionary<string, int> copies = new Dictionary<string, int>(StringComparer.Ordinal);
            public readonly List<string> Shown = new List<string>();

            /// <summary>Сколько деталей в подписи.</summary>
            public int Count
            {
                get { return this.seen.Count; }
            }

            public int CopiesOf(string name)
            {
                int count;
                return this.copies.TryGetValue(name ?? "", out count) ? count : 0;
            }

            public void Add(GdmlVolumeInstance volume)
            {
                if (volume == null || !this.seen.Add(volume))
                {
                    return;
                }

                string name = volume.VolumeName ?? "";
                int count;
                if (this.copies.TryGetValue(name, out count))
                {
                    this.copies[name] = count + 1;
                }
                else if (this.Shown.Count < MaxShown)
                {
                    this.Shown.Add(name);
                    this.copies[name] = 1;
                }
            }

            public void Clear()
            {
                this.seen.Clear();
                this.copies.Clear();
                this.Shown.Clear();
            }
        }

        static char AxisLetter(int index)
        {
            return index == 0 ? 'x' : index == 1 ? 'y' : 'z';
        }

        static string OutOfRangeText()
        {
            return GdmlText.T("GdmlSketchOutOfRange", "No drawing: the dimensions in the file are out of range");
        }

        /// <summary>
        /// Плоскость осевого разреза: ноль, пока он внутри габарита, иначе середина габарита.
        /// Прибор, целиком стоящий в стороне от нуля, иначе давал бы пустой вид.
        /// </summary>
        double AxialPlane(int normal)
        {
            double low = double.MaxValue, high = double.MinValue;
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume.BoundsKnown && !this.Overview.IsOutOfFrame(volume))
                {
                    low = Math.Min(low, volume.Min[normal]);
                    high = Math.Max(high, volume.Max[normal]);
                }
            }

            return low > high || (low <= 0.0 && high >= 0.0) ? 0.0 : low / 2.0 + high / 2.0;
        }

        ViewFrame MakeFrame(Rectangle rect, int u, int v)
        {
            this.frameProblem = null;
            double umin = double.MaxValue, umax = double.MinValue;
            double vmin = double.MaxValue, vmax = double.MinValue;
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (!volume.BoundsKnown)
                {
                    continue;
                }

                // Тело больше всего остального в тысячи раз: рамка по нему сводила прибор в точку. Оболочка вокруг всей
                // сцены (`AMBER301`, fixGdml2): рамка по миру 3000 × 500 × 3000 мм сводила прибор `lht.gdml` в пятно.
                if (this.Overview.IsOutOfFrame(volume))
                {
                    continue;
                }

                for (int axis = 0; axis < 3; axis++)
                {
                    if (!IsFinite(volume.Min[axis]) || !IsFinite(volume.Max[axis]))
                    {
                        this.frameProblem = OutOfRangeText();
                        return null;
                    }
                }

                umin = Math.Min(umin, volume.Min[u]);
                umax = Math.Max(umax, volume.Max[u]);
                vmin = Math.Min(vmin, volume.Min[v]);
                vmax = Math.Max(vmax, volume.Max[v]);
            }

            if (umin > umax || vmin > vmax)
            {
                this.frameProblem = GdmlText.T("GdmlSketchNoBounds",
                    "No drawing: no body in the file has known dimensions");
                return null;
            }

            if (umax - umin < 1e-6)
            {
                umax = umin + 1.0;
            }

            if (vmax - vmin < 1e-6)
            {
                vmax = vmin + 1.0;
            }

            // Места под рамку нет: отрицательный масштаб перевернул бы чертёж.
            if (rect.Width <= 2.0 * FrameMargin || rect.Height <= 2.0 * FrameMargin)
            {
                return null;
            }

            ViewFrame frame = ViewFrame.Create(rect, umin, umax, vmin, vmax, FrameMargin, this.commonScale);
            if (frame == null)
            {
                this.frameProblem = OutOfRangeText();
            }

            return frame;
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        void DrawVolume(Graphics graphics, ViewFrame frame, GdmlVolumeInstance volume,
                        int normal, int u, int v, double plane)
        {
            // Неизвестны форма, положение или границы: что и где рисовать, не установлено. Мерка — читателя, та же, что у
            // сводки (`AMBER301`, fixGdml2): незнакомое тело в составе вычитания или объединения — тоже неподдержанное, и
            // такое тело не рисуется вовсе, а не первой частью без выреза.
            if (GdmlGeometry.IsUnsupported(volume))
            {
                AddName(this.notDrawn, volume);
                return;
            }

            if (GdmlGeometry.IsUnplaced(volume))
            {
                AddName(this.unplaced, volume);
                return;
            }

            if (this.Overview.IsTrimmed(volume))
            {
                AddName(this.trimmed, volume);
                return;
            }

            if (this.Overview.IsEnvelope(volume))
            {
                AddName(this.envelopes, volume);
                return;
            }

            if (!IntersectsPlane(volume, normal, plane))
            {
                return;
            }

            if (!string.IsNullOrEmpty(volume.UnknownContent))
            {
                AddName(this.contentHidden, volume);
                this.contentReasons.Add(volume.UnknownContent);
            }

            // Неконечное положение при конечных границах: фигуры нет, и «плоскость не задевает» солгало бы.
            // Точка (1, 1, 1) — ради поворота: образ начала координат его не видит.
            GdmlPoint origin = volume.Transform.Apply(new GdmlPoint(1.0, 1.0, 1.0));
            if (!IsFinite(origin.X) || !IsFinite(origin.Y) || !IsFinite(origin.Z))
            {
                AddName(this.noSection, volume);
                return;
            }

            // ⛔ (`AMBER292`) Решается ДО рисования и без рекурсии: цепочка в 2000 вычитаний в 154 КБ файла
            // переполняла стек, а с этим заслон в OnPaint ничего сделать не может; частично нарисованное
            // тело было бы другой фигурой, поэтому тело либо целиком, либо подпись.
            if (volume.Solid.Kind == GdmlSolidKind.Subtraction || volume.Solid.Kind == GdmlSolidKind.Union)
            {
                ShapeCost cost = this.CostOf(volume.Solid);

                // Часть тела отвергнута читателем (плоскости не по порядку): вырез не определён,
                // и наложение, нарисованное как будто вырезанным, было бы другой фигурой.
                if (cost.Rejected)
                {
                    AddName(this.noSection, volume);
                    return;
                }

                long work = Math.Max(0, cost.Depth - 2) * cost.Vertices;
                if (cost.Depth > MaxShapeDepth || cost.Parts > MaxShapeParts || this.viewParts + cost.Parts > MaxViewParts
                    || this.viewWork + work > MaxViewWork
                    || this.viewVertices + cost.Vertices > MaxViewVertices)
                {
                    AddName(this.tooComplex, volume);
                    return;
                }

                this.viewParts += cost.Parts;
                this.viewWork += work;
                this.viewVertices += cost.Vertices;
            }

            ViewContext view = new ViewContext
            {
                Graphics = graphics,
                Frame = frame,
                Volume = volume,
                Normal = normal,
                U = u,
                V = v,
                Plane = plane,
            };
            try
            {
                this.DrawShape(view, volume.Solid, volume.Transform);
            }
            finally
            {
                view.ReleaseCache();
            }
        }

        struct ShapeCost
        {
            public int Depth;
            public long Parts;

            /// <summary>Вершин сечения в развёртке, с насыщением: 2^i при L(i) = L(i−1) − L(i−1).</summary>
            public long Vertices;

            /// <summary>Часть тела отвергнута читателем.</summary>
            public bool Rejected;
        }

        /// <summary>Вершин в сечении одного простого тела: полигон на отрезок и его зеркало.</summary>
        static long OwnVertices(GdmlSolid solid)
        {
            switch (solid.Kind)
            {
                case GdmlSolidKind.Box:
                    return 4;
                case GdmlSolidKind.Polycone:
                    return 8L * Math.Max(1, solid.Planes.Count - 1);
                case GdmlSolidKind.GenericPolycone:
                    return 2L * solid.Profile.Count;
                case GdmlSolidKind.Tessellated:
                    return solid.Triangles.Count;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Глубина и размер развёртки тела в дерево: явным стеком, с памятью по телу. Сам обход не
        /// рекурсивен нарочно, он и есть проверка перед рекурсией. Цикл читатель отсекает своим
        /// обходом; здесь он на всякий случай — за пределом.
        /// </summary>
        ShapeCost CostOf(GdmlSolid root)
        {
            // Насыщение: глубина на вершины не должна переполнить long.
            const long VerticesCap = long.MaxValue / (MaxShapeDepth + 2) / 2;
            ShapeCost known;
            if (this.shapeCosts.TryGetValue(root, out known))
            {
                return known;
            }

            Stack<GdmlSolid> stack = new Stack<GdmlSolid>();
            HashSet<GdmlSolid> open = new HashSet<GdmlSolid>();
            stack.Push(root);
            open.Add(root);
            while (stack.Count > 0)
            {
                GdmlSolid solid = stack.Peek();
                bool ready = true;
                int depth = 0;
                long parts = 1;
                long vertices = OwnVertices(solid);
                bool rejected = solid.Unreadable;
                foreach (GdmlSolid child in Parts(solid))
                {
                    ShapeCost cost;
                    if (this.shapeCosts.TryGetValue(child, out cost))
                    {
                        depth = Math.Max(depth, cost.Depth);
                        parts = Math.Min(MaxViewParts + 1, parts + cost.Parts);
                        vertices = Math.Min(VerticesCap, vertices + cost.Vertices);
                        rejected |= cost.Rejected;
                    }
                    else if (open.Add(child))
                    {
                        // По одному ребёнку: тогда в `open` только путь от корня, а общее тело цикла не изображает.
                        stack.Push(child);
                        ready = false;
                        break;
                    }
                    else
                    {
                        depth = MaxShapeDepth + 1;
                    }
                }

                if (ready)
                {
                    this.shapeCosts[solid] = new ShapeCost { Depth = depth + 1, Parts = parts, Vertices = vertices, Rejected = rejected };
                    stack.Pop();
                    open.Remove(solid);
                }
            }

            return this.shapeCosts[root];
        }

        static IEnumerable<GdmlSolid> Parts(GdmlSolid solid)
        {
            if (!IsBoolean(solid))
            {
                yield break;
            }

            if (solid.First != null)
            {
                yield return solid.First;
            }

            foreach (GdmlSolid second in solid.Second)
            {
                if (second != null)
                {
                    yield return second;
                }
            }
        }

        static bool IsBoolean(GdmlSolid solid)
        {
            return solid.Kind == GdmlSolidKind.Subtraction || solid.Kind == GdmlSolidKind.Union;
        }

        static void AddName(NameSet names, GdmlVolumeInstance volume)
        {
            names.Add(volume);
        }

        /// <summary>Что общего у всех фигур одного тома в одном виде.</summary>
        sealed class ViewContext
        {
            public Graphics Graphics;
            public ViewFrame Frame;
            public GdmlVolumeInstance Volume;
            public int Normal, U, V;
            public double Plane;

            /// <summary>Собранные области тома: одно тело в одном положении строится один раз.</summary>
            public Dictionary<ShapeKey, CollectedShape> Cache;

            public void ReleaseCache()
            {
                if (this.Cache == null)
                {
                    return;
                }

                foreach (CollectedShape shape in this.Cache.Values)
                {
                    shape.Area.Dispose();
                    shape.Lines.Dispose();
                }

                this.Cache = null;
            }
        }

        /// <summary>Сечение одной части тела: область и линии сетки.</summary>
        sealed class CollectedShape
        {
            public readonly Region Area = new Region();
            public readonly GraphicsPath Lines = new GraphicsPath();

            public CollectedShape()
            {
                this.Area.MakeEmpty();
            }
        }

        /// <summary>Тело и положение по значению: положение части каждый раз собирается заново, а равно прежнему.</summary>
        sealed class ShapeKey : IEquatable<ShapeKey>
        {
            readonly GdmlSolid solid;
            readonly double[] image = new double[12];

            public ShapeKey(GdmlSolid solid, GdmlTransform transform)
            {
                this.solid = solid;
                Put(0, transform.Apply(new GdmlPoint(0.0, 0.0, 0.0)));
                Put(3, transform.Apply(new GdmlPoint(1.0, 0.0, 0.0)));
                Put(6, transform.Apply(new GdmlPoint(0.0, 1.0, 0.0)));
                Put(9, transform.Apply(new GdmlPoint(0.0, 0.0, 1.0)));
            }

            void Put(int at, GdmlPoint point)
            {
                this.image[at] = point.X;
                this.image[at + 1] = point.Y;
                this.image[at + 2] = point.Z;
            }

            public bool Equals(ShapeKey other)
            {
                if (other == null || !ReferenceEquals(this.solid, other.solid))
                {
                    return false;
                }

                for (int i = 0; i < this.image.Length; i++)
                {
                    if (!this.image[i].Equals(other.image[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                return this.Equals(obj as ShapeKey);
            }

            public override int GetHashCode()
            {
                int hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this.solid);
                foreach (double value in this.image)
                {
                    hash = hash * 31 + value.GetHashCode();
                }

                return hash;
            }
        }

        /// <summary>
        /// Тело в положении <paramref name="transform"/>. Вычитание вырезается клипом, а не
        /// закрашивается: заливка цветом материнского тома затирала соседей в вырезе и рисовала
        /// контур там, где второе тело выступает за первое. Положения частей — из самого
        /// вычитания: читатель хранит их вровень с <see cref="GdmlSolid.Second"/>; прежде чертёж
        /// их не читал и рисовал вырез в начале координат тома.
        /// </summary>
        void DrawShape(ViewContext view, GdmlSolid solid, GdmlTransform transform)
        {
            Graphics graphics = view.Graphics;
            if (solid.Kind == GdmlSolidKind.Unknown || (IsBoolean(solid) && solid.First == null))
            {
                AddName(this.noSection, view.Volume);
                return;
            }

            if (solid.Kind == GdmlSolidKind.Union)
            {
                this.DrawUnion(view, solid, transform);
                return;
            }

            if (solid.Kind != GdmlSolidKind.Subtraction)
            {
                Color color = this.FillOf(view.Volume);
                using (SolidBrush brush = new SolidBrush(color))
                using (Pen pen = new Pen(Darken(color), 0.9f))
                {
                    this.DrawPrimitive(view, solid, transform, SectionSink.ToFill(graphics, brush, pen));
                }

                return;
            }

            GdmlTransform firstAt = FirstAt(solid, transform);
            using (Region cut = this.CollectSeconds(view, solid, transform))
            {
                GraphicsState state = graphics.Save();
                graphics.ExcludeClip(cut);
                this.DrawShape(view, solid.First, firstAt);
                graphics.Restore(state);
            }

            // Контур выреза — только по телу: выступающая часть второго тела выреза не делает.
            using (Region first = new Region())
            using (GraphicsPath firstLines = new GraphicsPath())
            using (Pen pen = new Pen(Darken(this.ParentColor(view.Volume)), 1.6f))
            {
                first.MakeEmpty();
                this.CollectShape(view, solid.First, firstAt, first, firstLines);
                GraphicsState state = graphics.Save();
                graphics.IntersectClip(first);
                for (int i = 0; i < solid.Second.Count; i++)
                {
                    this.OutlineShape(view, solid.Second[i], SecondAt(solid, i, transform), pen);
                }

                graphics.Restore(state);
            }
        }

        /// <summary>
        /// Объединение — одна фигура: заливка по общей области, контур — край самой этой области
        /// (<see cref="OutlineFromArea"/>). Иначе граница части легла бы внутрь тела, как будто это
        /// два тома. Сетка площади в разрезе не имеет, поэтому её линии рисуются как есть.
        /// </summary>
        void DrawUnion(ViewContext view, GdmlSolid solid, GdmlTransform transform)
        {
            Graphics graphics = view.Graphics;
            CollectedShape whole = this.CollectedOf(view, solid, transform);

            // Плоскость идёт в зазоре между частями: рисовать нечего, и «нарисовано одним телом» было бы неправдой.
            if (whole.Area.IsEmpty(graphics) && whole.Lines.PointCount == 0)
            {
                return;
            }

            AddName(this.unions, view.Volume);
            Color color = this.FillOf(view.Volume);
            using (SolidBrush brush = new SolidBrush(color))
            using (Pen pen = new Pen(Darken(color), 0.9f))
            {
                graphics.FillRegion(brush, whole.Area);
                if (whole.Lines.PointCount > 0)
                {
                    graphics.DrawPath(pen, whole.Lines);
                }

                this.OutlineFromArea(view, solid, transform, whole.Area, pen);
            }
        }

        /// <summary>
        /// Контур составного тела (`critGdml5`, Н-А): рёбра всех простых частей, оставленные там,
        /// где они и вправду край общей области. Прежняя запись «контур части вне другой части» верна
        /// только для двух простых тел: при трёх частях шов ложился внутрь тела и край пропадал, а у
        /// объединения с вычитанием контур шёл по вырезанному углу.
        ///
        /// Ребро идёт по отрезкам в 3 точки; отрезок — край, если хотя бы с одной стороны от него не
        /// область, а одна из сторон или он сам в области. Обе стороны внутри — шов; все три
        /// снаружи — ребро второго тела вычитания, выступающее за первое. Стоит только составным телам,
        /// настоящие файлы таких не содержат; при большом периметре шаг растёт, предел — <see cref="MaxContourSamples"/>.
        /// </summary>
        void OutlineFromArea(ViewContext view, GdmlSolid solid, GdmlTransform transform, Region area, Pen pen)
        {
            List<PointF[]> loops = new List<PointF[]>();
            this.CollectEdges(view, solid, transform, loops);
            RectangleF clip = view.Graphics.VisibleClipBounds;
            double perimeter = 0.0;
            foreach (PointF[] loop in loops)
            {
                for (int i = 0; i < loop.Length; i++)
                {
                    PointF a = loop[i], b = loop[(i + 1) % loop.Length];
                    if (ClipSegment(clip, ref a, ref b))
                    {
                        perimeter += Length(a, b);
                    }
                }
            }

            float step = (float)Math.Max(3.0, perimeter / MaxContourSamples);
            const float Side = 1.5f;
            using (GraphicsPath path = new GraphicsPath())
            {
                foreach (PointF[] loop in loops)
                {
                    for (int i = 0; i < loop.Length; i++)
                    {
                        PointF a = loop[i], b = loop[(i + 1) % loop.Length];
                        if (!ClipSegment(clip, ref a, ref b))
                        {
                            continue;
                        }

                        float length = (float)Length(a, b);
                        if (length < 1e-3f)
                        {
                            continue;
                        }

                        float ux = (b.X - a.X) / length, uy = (b.Y - a.Y) / length;
                        int pieces = Math.Max(1, (int)Math.Ceiling(length / step));
                        bool open = false;
                        PointF start = a;
                        for (int k = 0; k < pieces; k++)
                        {
                            float at = (k + 0.5f) * length / pieces;
                            float x = a.X + ux * at, y = a.Y + uy * at;
                            bool left = area.IsVisible(x - uy * Side, y + ux * Side);
                            bool right = area.IsVisible(x + uy * Side, y - ux * Side);
                            bool edge = !(left && right) && (left || right || area.IsVisible(x, y));
                            PointF here = new PointF(a.X + ux * k * length / pieces, a.Y + uy * k * length / pieces);
                            if (edge && !open)
                            {
                                start = here;
                                open = true;
                            }
                            else if (!edge && open)
                            {
                                path.StartFigure();
                                path.AddLine(start, here);
                                open = false;
                            }
                        }

                        if (open)
                        {
                            path.StartFigure();
                            path.AddLine(start, b);
                        }
                    }
                }

                if (path.PointCount > 0)
                {
                    view.Graphics.DrawPath(pen, path);
                }
            }
        }

        static double Length(PointF a, PointF b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Отрезок в прямоугольнике (Лианг — Барски): ребро за рамкой обходить незачем.</summary>
        static bool ClipSegment(RectangleF box, ref PointF a, ref PointF b)
        {
            double t0 = 0.0, t1 = 1.0;
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double[] p = { -dx, dx, -dy, dy };
            double[] q = { a.X - box.Left, box.Right - a.X, a.Y - box.Top, box.Bottom - a.Y };
            for (int i = 0; i < 4; i++)
            {
                if (p[i] == 0.0)
                {
                    if (q[i] < 0.0)
                    {
                        return false;
                    }

                    continue;
                }

                double r = q[i] / p[i];
                if (p[i] < 0.0)
                {
                    t0 = Math.Max(t0, r);
                }
                else
                {
                    t1 = Math.Min(t1, r);
                }

                if (t0 > t1)
                {
                    return false;
                }
            }

            PointF start = new PointF((float)(a.X + t0 * dx), (float)(a.Y + t0 * dy));
            b = new PointF((float)(a.X + t1 * dx), (float)(a.Y + t1 * dy));
            a = start;
            return true;
        }

        /// <summary>Рёбра всех простых частей составного тела, в точках экрана. Глубину ограничил <see cref="CostOf"/>.</summary>
        void CollectEdges(ViewContext view, GdmlSolid solid, GdmlTransform transform, List<PointF[]> loops)
        {
            if (solid == null || solid.Kind == GdmlSolidKind.Unknown || (IsBoolean(solid) && solid.First == null))
            {
                return;
            }

            if (!IsBoolean(solid))
            {
                this.DrawPrimitive(view, solid, transform, SectionSink.ToEdges(loops));
                return;
            }

            this.CollectEdges(view, solid.First, FirstAt(solid, transform), loops);
            for (int i = 0; i < solid.Second.Count; i++)
            {
                this.CollectEdges(view, solid.Second[i], SecondAt(solid, i, transform), loops);
            }
        }

        static GdmlTransform FirstAt(GdmlSolid solid, GdmlTransform transform)
        {
            return solid.FirstPlacement == null ? transform : transform.Compose(solid.FirstPlacement);
        }

        static GdmlTransform SecondAt(GdmlSolid solid, int index, GdmlTransform transform)
        {
            return index < solid.SecondPlacements.Count && solid.SecondPlacements[index] != null
                ? transform.Compose(solid.SecondPlacements[index])
                : transform;
        }

        Region CollectSeconds(ViewContext view, GdmlSolid solid, GdmlTransform transform)
        {
            Region region = new Region();
            region.MakeEmpty();
            for (int i = 0; i < solid.Second.Count; i++)
            {
                using (GraphicsPath lines = new GraphicsPath())
                {
                    this.CollectShape(view, solid.Second[i], SecondAt(solid, i, transform), region, lines);

                    // У сетки в разрезе только линии, площади нет: вырезать нечем.
                    if (lines.PointCount > 0)
                    {
                        AddName(this.noSection, view.Volume);
                    }
                }
            }

            return region;
        }

        void CollectShape(ViewContext view, GdmlSolid solid, GdmlTransform transform, Region region, GraphicsPath lines)
        {
            if (solid.Kind == GdmlSolidKind.Unknown || (IsBoolean(solid) && solid.First == null))
            {
                AddName(this.noSection, view.Volume);
                return;
            }

            CollectedShape collected = this.CollectedOf(view, solid, transform);
            region.Union(collected.Area);
            if (collected.Lines.PointCount > 0)
            {
                lines.AddPath(collected.Lines, false);
            }
        }

        /// <summary>
        /// ⛔ (`AMBER292`) С памятью: без неё L(i) = L(i−1) − L(i−1) в 14 уровней занимало 33 с на одну
        /// перерисовку в потоке окна, а цепочка из 255 вычитаний — 1.5 с, квадратично.
        /// </summary>
        CollectedShape CollectedOf(ViewContext view, GdmlSolid solid, GdmlTransform transform)
        {
            if (view.Cache == null)
            {
                view.Cache = new Dictionary<ShapeKey, CollectedShape>();
            }

            ShapeKey key = new ShapeKey(solid, transform);
            CollectedShape collected;
            if (view.Cache.TryGetValue(key, out collected))
            {
                return collected;
            }

            this.regionsBuilt++;
            collected = new CollectedShape();
            view.Cache[key] = collected;
            if (solid.Kind == GdmlSolidKind.Union)
            {
                this.CollectShape(view, solid.First, FirstAt(solid, transform), collected.Area, collected.Lines);
                for (int i = 0; i < solid.Second.Count; i++)
                {
                    this.CollectShape(view, solid.Second[i], SecondAt(solid, i, transform), collected.Area, collected.Lines);
                }

                return collected;
            }

            if (solid.Kind != GdmlSolidKind.Subtraction)
            {
                this.DrawPrimitive(view, solid, transform, SectionSink.ToRegion(collected.Area, collected.Lines));
                return collected;
            }

            this.CollectShape(view, solid.First, FirstAt(solid, transform), collected.Area, collected.Lines);
            using (Region cut = this.CollectSeconds(view, solid, transform))
            {
                collected.Area.Exclude(cut);
            }

            return collected;
        }

        void OutlineShape(ViewContext view, GdmlSolid solid, GdmlTransform transform, Pen pen)
        {
            if (solid.Kind == GdmlSolidKind.Unknown || (IsBoolean(solid) && solid.First == null))
            {
                return;
            }

            if (IsBoolean(solid))
            {
                this.OutlineFromArea(view, solid, transform, this.CollectedOf(view, solid, transform).Area, pen);
            }
            else
            {
                this.DrawPrimitive(view, solid, transform, SectionSink.ToOutline(view.Graphics, pen));
            }
        }

        void DrawPrimitive(ViewContext view, GdmlSolid solid, GdmlTransform transform, SectionSink sink)
        {
            long clamped = view.Frame.Clamped;
            switch (solid.Kind)
            {
                case GdmlSolidKind.Box:
                    DrawBox(view, solid, transform, sink);
                    break;
                case GdmlSolidKind.Polycone:
                case GdmlSolidKind.GenericPolycone:
                    this.DrawRevolution(view, solid, transform, sink);
                    break;
                case GdmlSolidKind.Tessellated:
                    this.DrawMesh(view, solid, transform, sink);
                    break;
            }

            sink.Flush();
            this.shapesDrawn += sink.Drawn;

            // Зажатая координата сдвигает вершину и молча меняет фигуру: вырез под 30° длиной 1e5 мм
            // давал 357 мм² вместо 308. Вне рамки это безвредно, но здесь этого не отличить: подпись.
            if (view.Frame.Clamped != clamped)
            {
                AddName(this.noSection, view.Volume);
            }
        }

        static bool IntersectsPlane(GdmlVolumeInstance volume, int normal, double plane)
        {
            if (!volume.BoundsKnown)
            {
                return true;
            }

            double low = volume.Min[normal];
            double high = volume.Max[normal];
            double tolerance = 1e-6 * Math.Max(1.0, Math.Max(Math.Abs(low), Math.Abs(high)));
            return low - tolerance <= plane && plane <= high + tolerance;
        }

        // ------------------------------------------------------------------
        // Коробка
        // ------------------------------------------------------------------

        static void DrawBox(ViewContext view, GdmlSolid solid, GdmlTransform transform, SectionSink sink)
        {
            double hx = solid.SizeX / 2.0;
            double hy = solid.SizeY / 2.0;
            double hz = solid.SizeZ / 2.0;
            GdmlPoint[] corners = new GdmlPoint[8];
            for (int i = 0; i < 8; i++)
            {
                corners[i] = transform.Apply(new GdmlPoint(
                    (i & 1) == 0 ? -hx : hx,
                    (i & 2) == 0 ? -hy : hy,
                    (i & 4) == 0 ? -hz : hz));
            }

            double[] distance = new double[8];
            for (int i = 0; i < 8; i++)
            {
                distance[i] = Coordinate(corners[i], view.Normal) - view.Plane;
            }

            List<PointF> points = new List<PointF>();
            for (int i = 0; i < 8; i++)
            {
                if (Math.Abs(distance[i]) < 1e-7)
                {
                    AddProjected(points, view.Frame, corners[i], view.U, view.V);
                }
            }

            int[,] edges =
            {
                { 0, 1 }, { 0, 2 }, { 0, 4 }, { 1, 3 }, { 1, 5 }, { 2, 3 },
                { 2, 6 }, { 3, 7 }, { 4, 5 }, { 4, 6 }, { 5, 7 }, { 6, 7 },
            };
            for (int i = 0; i < edges.GetLength(0); i++)
            {
                int a = edges[i, 0];
                int b = edges[i, 1];
                if (distance[a] * distance[b] < 0.0)
                {
                    double t = distance[a] / (distance[a] - distance[b]);
                    GdmlPoint point = new GdmlPoint(
                        corners[a].X + t * (corners[b].X - corners[a].X),
                        corners[a].Y + t * (corners[b].Y - corners[a].Y),
                        corners[a].Z + t * (corners[b].Z - corners[a].Z));
                    AddProjected(points, view.Frame, point, view.U, view.V);
                }
            }

            // Сечение коробки выпукло, а точки собраны вразнобой: только ему нужен обход по углу.
            sink.Polygon(SortedAroundCenter(points));
        }

        // ------------------------------------------------------------------
        // Тела вращения
        // ------------------------------------------------------------------

        /// <summary>
        /// Сечение тела вращения плоскостью вида. Ось тела в мире — образ локальной z: чертёж
        /// смотрит на готовое преобразование и не знает, из каких углов оно собрано.
        ///   ось в плоскости — профиль, на расстоянии от оси — хорды;
        ///   ось перпендикулярна плоскости — кольца;
        ///   иначе сечение — эллипсы и кривые: подпись-отказ, а не другая фигура.
        /// </summary>
        void DrawRevolution(ViewContext view, GdmlSolid solid, GdmlTransform transform, SectionSink sink)
        {
            RevolutionFrame body = new RevolutionFrame(transform);
            double cosine = Coordinate(body.Z, view.Normal);
            if (IsFinite(cosine) && Math.Abs(cosine) < AxisTolerance)
            {
                this.DrawLongitudinal(view, solid, body, sink);
            }
            else if (IsFinite(cosine) && 1.0 - cosine * cosine < AxisTolerance * AxisTolerance)
            {
                DrawTransverse(view, solid, body, cosine, sink);
            }
            else
            {
                AddName(this.noSection, view.Volume);
            }
        }

        /// <summary>Система тела вращения в мире: начало и образы локальных осей. Зеркало входит само.</summary>
        sealed class RevolutionFrame
        {
            public readonly GdmlPoint Origin, X, Y, Z;

            public RevolutionFrame(GdmlTransform transform)
            {
                this.Origin = transform.Apply(new GdmlPoint(0.0, 0.0, 0.0));
                this.X = this.Direction(transform.Apply(new GdmlPoint(1.0, 0.0, 0.0)));
                this.Y = this.Direction(transform.Apply(new GdmlPoint(0.0, 1.0, 0.0)));
                this.Z = this.Direction(transform.Apply(new GdmlPoint(0.0, 0.0, 1.0)));
            }

            GdmlPoint Direction(GdmlPoint image)
            {
                return new GdmlPoint(image.X - this.Origin.X, image.Y - this.Origin.Y, image.Z - this.Origin.Z);
            }
        }

        /// <summary>
        /// Ось в плоскости. На расстоянии d от оси точка профиля (r, z) попадает в плоскость при
        /// s = ±sqrt(r² − d²): при d = 0 это сам профиль и его зеркало, иначе хорда. Неполный
        /// оборот: на самой оси половина либо целиком в секторе, либо вне его; на хорде угол
        /// меняется вдоль сечения, и точного сечения здесь нет.
        /// </summary>
        void DrawLongitudinal(ViewContext view, GdmlSolid solid, RevolutionFrame body, SectionSink sink)
        {
            double normalX = view.Normal == 0 ? 1.0 : 0.0;
            double normalY = view.Normal == 1 ? 1.0 : 0.0;
            double normalZ = view.Normal == 2 ? 1.0 : 0.0;
            double distance = view.Plane - Coordinate(body.Origin, view.Normal);
            bool onAxis = Math.Abs(distance) < 1e-6;

            // Плоскость вообще мимо этого тела (часть объединения в стороне): ни хорды, ни подписи о ней.
            double reach = 0.0;
            foreach (double[] plane in solid.Planes)
            {
                reach = Math.Max(reach, plane[2]);
            }

            foreach (double[] point in solid.Profile)
            {
                reach = Math.Max(reach, point[0]);
            }

            if (!onAxis && Math.Abs(distance) >= reach)
            {
                return;
            }

            bool partial = solid.DeltaPhi < 360.0;
            if (partial && !onAxis)
            {
                AddName(this.noSection, view.Volume);
                return;
            }

            if (!onAxis)
            {
                AddName(this.offAxis, view.Volume);
            }

            // s: вдоль плоскости поперёк оси, n × z.
            GdmlPoint s = new GdmlPoint(
                normalY * body.Z.Z - normalZ * body.Z.Y,
                normalZ * body.Z.X - normalX * body.Z.Z,
                normalX * body.Z.Y - normalY * body.Z.X);

            // Угол полуплоскости +s в системе тела; зеркальная половина — на 180° дальше.
            double phi = Math.Atan2(Dot(s, body.Y), Dot(s, body.X)) * 180.0 / Math.PI;
            bool plus = !partial || AngleInside(phi, solid.StartPhi, solid.DeltaPhi);
            bool minus = !partial || AngleInside(phi + 180.0, solid.StartPhi, solid.DeltaPhi);

            GdmlPoint foot = new GdmlPoint(
                body.Origin.X + distance * normalX,
                body.Origin.Y + distance * normalY,
                body.Origin.Z + distance * normalZ);

            if (solid.Kind == GdmlSolidKind.Polycone)
            {
                // Одним многоугольником: наружу по rmax, обратно по rmin. Прежде по четырёхугольнику на
                // пару плоскостей, и граница каждого ложилась поперёк тела швом.
                int planes = solid.Planes.Count;
                double[][] outline = new double[2 * planes][];
                for (int i = 0; i < planes; i++)
                {
                    double[] plane = solid.Planes[i];
                    outline[i] = new[] { plane[2], plane[0] };
                    outline[2 * planes - 1 - i] = new[] { plane[1], plane[0] };
                }

                DrawHalves(view, body, foot, s, outline, distance, onAxis, plus, minus, sink);
            }
            else
            {
                DrawHalves(view, body, foot, s, solid.Profile.ToArray(), distance, onAxis, plus, minus, sink);
            }
        }

        static void DrawHalves(ViewContext view, RevolutionFrame body, GdmlPoint foot, GdmlPoint s,
                               double[][] profile, double distance, bool onAxis, bool plus, bool minus,
                               SectionSink sink)
        {
            if (profile.Length < 3)
            {
                return;
            }

            // ⛔ Порядок файла: вогнутый профиль (стакан) обходом по углу превращался в другую
            // фигуру, площадь до ×7.92 (`AMBER304`).
            List<double[]> section = onAxis ? new List<double[]>(profile) : ChordProfile(profile, distance);
            if (section.Count < 3)
            {
                return;
            }

            PointF[] plusSide = plus ? ProfileToScreen(view, body, foot, s, section, 1.0) : null;
            PointF[] minusSide = minus ? ProfileToScreen(view, body, foot, s, section, -1.0) : null;

            // Обе половины есть: ребро на самой оси — не край тела, а шов между ними. Половина одна
            // (неполный оборот): то же ребро и есть край, его срезала плоскость сектора.
            bool[] seam = null;
            if (plus && minus)
            {
                double largest = 0.0;
                foreach (double[] point in section)
                {
                    largest = Math.Max(largest, point[0]);
                }

                // Хорда: r² − d² на грани округления даёт до 1e-8 r.
                double onSeam = 1e-7 * largest;
                seam = new bool[section.Count];
                for (int i = 0; i < seam.Length; i++)
                {
                    seam[i] = section[i][0] <= onSeam && section[(i + 1) % seam.Length][0] <= onSeam;
                }
            }

            sink.Halves(plusSide, minusSide, seam);
        }

        /// <summary>
        /// Профиль (r, z) → половина хорды (s, z). Сперва срез по r ≥ |d| (ближе плоскость тела
        /// не задевает), затем дробление рёбер: отображение нелинейно, и прямая между вершинами
        /// дала бы другую фигуру.
        /// </summary>
        static List<double[]> ChordProfile(double[][] profile, double distance)
        {
            const int Steps = 24;
            double limit = Math.Abs(distance);
            List<double[]> clipped = new List<double[]>();
            for (int i = 0; i < profile.Length; i++)
            {
                double[] a = profile[i];
                double[] b = profile[(i + 1) % profile.Length];
                bool aInside = a[0] >= limit;
                bool bInside = b[0] >= limit;
                if (aInside)
                {
                    clipped.Add(a);
                }

                if (aInside != bInside)
                {
                    double t = (limit - a[0]) / (b[0] - a[0]);
                    clipped.Add(new[] { limit, a[1] + t * (b[1] - a[1]) });
                }
            }

            List<double[]> section = new List<double[]>();
            for (int i = 0; i < clipped.Count; i++)
            {
                double[] a = clipped[i];
                double[] b = clipped[(i + 1) % clipped.Count];
                int steps = Math.Abs(a[0] - b[0]) > 1e-12 ? Steps : 1;
                for (int k = 0; k < steps; k++)
                {
                    double t = (double)k / steps;
                    double r = a[0] + t * (b[0] - a[0]);
                    section.Add(new[]
                    {
                        Math.Sqrt(Math.Max(0.0, r * r - distance * distance)),
                        a[1] + t * (b[1] - a[1]),
                    });
                }
            }

            return section;
        }

        static PointF[] ProfileToScreen(ViewContext view, RevolutionFrame body, GdmlPoint foot, GdmlPoint s,
                                        List<double[]> section, double sign)
        {
            PointF[] points = new PointF[section.Count];
            for (int i = 0; i < section.Count; i++)
            {
                double along = section[i][0] * sign;
                double height = section[i][1];
                GdmlPoint world = new GdmlPoint(
                    foot.X + along * s.X + height * body.Z.X,
                    foot.Y + along * s.Y + height * body.Z.Y,
                    foot.Z + along * s.Z + height * body.Z.Z);
                points[i] = view.Frame.ToScreen(Coordinate(world, view.U), Coordinate(world, view.V));
            }

            return points;
        }

        static double Dot(GdmlPoint a, GdmlPoint b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        static bool AngleInside(double angle, double start, double delta)
        {
            const double Tolerance = 1e-6;
            double offset = (angle - start) % 360.0;
            if (offset < 0.0)
            {
                offset += 360.0;
            }

            return offset <= delta + Tolerance || offset >= 360.0 - Tolerance;
        }

        /// <summary>Ось поперёк плоскости: на локальной высоте плоскости лежат кольца.</summary>
        static void DrawTransverse(ViewContext view, GdmlSolid solid, RevolutionFrame body, double cosine, SectionSink sink)
        {
            double localZ = (view.Plane - Coordinate(body.Origin, view.Normal)) * cosine;
            if (solid.Kind == GdmlSolidKind.Polycone)
            {
                for (int i = 0; i + 1 < solid.Planes.Count; i++)
                {
                    double[] a = solid.Planes[i];
                    double[] b = solid.Planes[i + 1];
                    if (!Between(localZ, a[0], b[0]))
                    {
                        continue;
                    }

                    double rmin, rmax;
                    Interpolate(a[0], a[1], a[2], b[0], b[1], b[2], localZ, out rmin, out rmax);
                    DrawRing(view, solid, body, localZ, rmin, rmax, sink);
                }
            }
            else
            {
                // Вершина профиля ровно на высоте плоскости считается один раз: нижний конец
                // ребра входит, верхний нет. Иначе два равных радиуса сбивали пары колец.
                List<double> radii = ProfileCrossings(solid.Profile, localZ, false);
                if (radii.Count == 0)
                {
                    radii = ProfileCrossings(solid.Profile, localZ, true);
                }

                radii.Sort();
                for (int i = 0; i + 1 < radii.Count; i += 2)
                {
                    DrawRing(view, solid, body, localZ, radii[i], radii[i + 1], sink);
                }
            }
        }

        static List<double> ProfileCrossings(List<double[]> profile, double z, bool topEnd)
        {
            List<double> radii = new List<double>();
            for (int i = 0; i < profile.Count; i++)
            {
                double[] a = profile[i];
                double[] b = profile[(i + 1) % profile.Count];
                double low = Math.Min(a[1], b[1]);
                double high = Math.Max(a[1], b[1]);
                bool inside = topEnd ? z > low && z <= high : z >= low && z < high;
                if (inside)
                {
                    radii.Add(a[0] + (z - a[1]) / (b[1] - a[1]) * (b[0] - a[0]));
                }
            }

            return radii;
        }

        static void DrawRing(ViewContext view, GdmlSolid solid, RevolutionFrame body, double localZ,
                             double rmin, double rmax, SectionSink sink)
        {
            GdmlPoint center = new GdmlPoint(
                body.Origin.X + localZ * body.Z.X,
                body.Origin.Y + localZ * body.Z.Y,
                body.Origin.Z + localZ * body.Z.Z);
            PointF screen = view.Frame.ToScreen(Coordinate(center, view.U), Coordinate(center, view.V));
            if (solid.DeltaPhi >= 360.0)
            {
                sink.Annulus(view.Frame, screen, rmin, rmax);
                return;
            }

            // Неполный оборот — сектор кольца; углы — в системе тела, поворот и зеркало учтены.
            const double StepDegrees = 2.0;
            int steps = Math.Max(1, (int)Math.Ceiling(solid.DeltaPhi / StepDegrees));
            List<PointF> points = new List<PointF>();
            for (int k = 0; k <= steps; k++)
            {
                points.Add(SectorPoint(view, body, center, rmax, solid.StartPhi + solid.DeltaPhi * k / steps));
            }

            if (rmin <= 0.0)
            {
                points.Add(screen);
            }
            else
            {
                for (int k = steps; k >= 0; k--)
                {
                    points.Add(SectorPoint(view, body, center, rmin, solid.StartPhi + solid.DeltaPhi * k / steps));
                }
            }

            sink.Polygon(points.ToArray());
        }

        static PointF SectorPoint(ViewContext view, RevolutionFrame body, GdmlPoint center, double r, double degrees)
        {
            double c = r * Math.Cos(degrees * Math.PI / 180.0);
            double s = r * Math.Sin(degrees * Math.PI / 180.0);
            GdmlPoint world = new GdmlPoint(
                center.X + c * body.X.X + s * body.Y.X,
                center.Y + c * body.X.Y + s * body.Y.Y,
                center.Z + c * body.X.Z + s * body.Y.Z);
            return view.Frame.ToScreen(Coordinate(world, view.U), Coordinate(world, view.V));
        }

        static bool Between(double value, double a, double b)
        {
            return value >= Math.Min(a, b) && value <= Math.Max(a, b);
        }

        static void Interpolate(double z1, double rmin1, double rmax1,
                                double z2, double rmin2, double rmax2, double z,
                                out double rmin, out double rmax)
        {
            if (Math.Abs(z2 - z1) < 1e-12)
            {
                rmin = rmin1;
                rmax = rmax1;
                return;
            }

            double t = (z - z1) / (z2 - z1);
            rmin = rmin1 + t * (rmin2 - rmin1);
            rmax = rmax1 + t * (rmax2 - rmax1);
        }

        // ------------------------------------------------------------------
        // Сетка tessellated
        // ------------------------------------------------------------------

        void DrawMesh(ViewContext view, GdmlSolid solid, GdmlTransform transform, SectionSink sink)
        {
            if (solid.Triangles.Count < 3 || solid.Vertices.Count == 0)
            {
                return;
            }

            using (GraphicsPath path = new GraphicsPath())
            {
                double[] distance = new double[3];
                GdmlPoint[] world = new GdmlPoint[3];

                for (int i = 0; i + 2 < solid.Triangles.Count; i += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        world[k] = transform.Apply(solid.Vertices[solid.Triangles[i + k]]);
                        distance[k] = Coordinate(world[k], view.Normal) - view.Plane;
                    }

                    if ((distance[0] > 0.0 && distance[1] > 0.0 && distance[2] > 0.0)
                        || (distance[0] < 0.0 && distance[1] < 0.0 && distance[2] < 0.0))
                    {
                        continue;
                    }

                    List<PointF> points = new List<PointF>();
                    for (int k = 0; k < 3; k++)
                    {
                        int next = (k + 1) % 3;
                        if (Math.Abs(distance[k]) < 1e-9)
                        {
                            AddProjected(points, view.Frame, world[k], view.U, view.V);
                        }

                        if (distance[k] * distance[next] < 0.0)
                        {
                            double t = distance[k] / (distance[k] - distance[next]);
                            GdmlPoint point = new GdmlPoint(
                                world[k].X + t * (world[next].X - world[k].X),
                                world[k].Y + t * (world[next].Y - world[k].Y),
                                world[k].Z + t * (world[next].Z - world[k].Z));
                            AddProjected(points, view.Frame, point, view.U, view.V);
                        }
                    }

                    // ⛔ Каждый отрезок — своя фигура: без StartFigure путь соединял конец одного
                    // с началом следующего хордой, которой в геометрии нет (`AMBER304`).
                    if (points.Count >= 2)
                    {
                        path.StartFigure();
                        path.AddLine(points[0], points[1]);

                        // Грань целиком в плоскости: сечение — весь её контур.
                        if (points.Count > 2)
                        {
                            path.AddLine(points[1], points[2]);
                            path.CloseFigure();
                        }
                    }
                }

                if (path.PointCount > 0)
                {
                    if (sink.WantsFill)
                    {
                        using (GraphicsPath area = this.MeshSection(view, solid, transform))
                        {
                            if (area != null)
                            {
                                sink.FillOnly(area);
                            }
                            else
                            {
                                AddName(this.linesOnly, view.Volume);
                            }
                        }
                    }

                    sink.Lines(path);
                }
            }
        }

        /// <summary>Вершины сетки, совпадающие по координатам, под одним номером. Один раз на тело.</summary>
        int[] MeshIds(GdmlSolid solid)
        {
            int[] ids;
            if (this.meshIds.TryGetValue(solid, out ids))
            {
                return ids;
            }

            // Ячейка 1e-6 мм. Вершины, разведённые читателем в допуске 0.5e-9 мм, почти всегда в одной
            // ячейке; иначе цепочка не сойдётся, и тело останется контуром, а не другой фигурой.
            const double Cell = 1e6;
            ids = new int[solid.Vertices.Count];
            Dictionary<VertexKey, int> known = new Dictionary<VertexKey, int>(ids.Length);
            for (int i = 0; i < ids.Length; i++)
            {
                GdmlPoint p = solid.Vertices[i];
                VertexKey key = new VertexKey(Math.Round(p.X * Cell), Math.Round(p.Y * Cell), Math.Round(p.Z * Cell));
                int id;
                if (!known.TryGetValue(key, out id))
                {
                    id = i;
                    known[key] = id;
                }

                ids[i] = id;
            }

            this.meshIds[solid] = ids;
            return ids;
        }

        struct VertexKey : IEquatable<VertexKey>
        {
            readonly double x, y, z;

            public VertexKey(double x, double y, double z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public bool Equals(VertexKey other)
            {
                return this.x == other.x && this.y == other.y && this.z == other.z;
            }

            public override bool Equals(object obj)
            {
                return obj is VertexKey && this.Equals((VertexKey)obj);
            }

            public override int GetHashCode()
            {
                return this.x.GetHashCode() * 486187739 + this.y.GetHashCode() * 16777619 + this.z.GetHashCode();
            }
        }

        sealed class MeshKeyComparer : IEqualityComparer<long>
        {
            public bool Equals(long a, long b)
            {
                return a == b;
            }

            public int GetHashCode(long key)
            {
                return (int)((key * unchecked((long)0x9E3779B97F4A7C15UL)) >> 32);
            }
        }

        /// <summary>Узел сечения: точка на ребре сетки и две стороны, куда идёт контур.</summary>
        sealed class MeshNode
        {
            public PointF Point;
            public long A = -1, B = -1;
            public int Links;
            public bool Seen;
        }

        /// <summary>
        /// Область сечения замкнутой сетки: обход по рёбрам сетки, а не по совпадению точек экрана.
        /// Вершина ровно на плоскости считается над нею, поэтому на замкнутой поверхности у каждого
        /// узла ровно две стороны и контур сходится. Правило чёт-нечет: полость внутри тела остаётся
        /// пустой. null — поверхность не замкнута или ветвится: области нет, и заливка была бы
        /// другой фигурой; такое тело остаётся линиями с подписью.
        /// </summary>
        GraphicsPath MeshSection(ViewContext view, GdmlSolid solid, GdmlTransform transform)
        {
            if (!solid.VolumeKnown)
            {
                return null;
            }

            int[] ids = this.MeshIds(solid);
            Dictionary<long, MeshNode> nodes = new Dictionary<long, MeshNode>(new MeshKeyComparer());
            GdmlPoint[] world = new GdmlPoint[3];
            double[] distance = new double[3];
            int[] id = new int[3];
            long[] crossing = new long[2];
            for (int i = 0; i + 2 < solid.Triangles.Count; i += 3)
            {
                int above = 0;
                for (int k = 0; k < 3; k++)
                {
                    int vertex = solid.Triangles[i + k];
                    id[k] = ids[vertex];
                    world[k] = transform.Apply(solid.Vertices[vertex]);
                    distance[k] = Coordinate(world[k], view.Normal) - view.Plane;
                    above += distance[k] >= 0.0 ? 1 : 0;
                }

                if (above == 0 || above == 3)
                {
                    continue;
                }

                int found = 0;
                for (int k = 0; k < 3; k++)
                {
                    int next = (k + 1) % 3;
                    if ((distance[k] >= 0.0) == (distance[next] >= 0.0))
                    {
                        continue;
                    }

                    // Точка считается от меньшего номера: обе грани ребра дают её побитово одинаково.
                    bool forward = id[k] < id[next];
                    int from = forward ? k : next, to = forward ? next : k;
                    long key = ((long)id[from] << 32) | (uint)id[to];
                    crossing[found++] = key;
                    if (!nodes.ContainsKey(key))
                    {
                        double t = distance[from] / (distance[from] - distance[to]);
                        GdmlPoint point = new GdmlPoint(
                            world[from].X + t * (world[to].X - world[from].X),
                            world[from].Y + t * (world[to].Y - world[from].Y),
                            world[from].Z + t * (world[to].Z - world[from].Z));
                        nodes[key] = new MeshNode { Point = view.Frame.ToScreen(Coordinate(point, view.U), Coordinate(point, view.V)) };
                    }
                }

                // Вырожденная грань (две вершины свелись в одну): отрезка нет.
                if (found == 2 && crossing[0] == crossing[1])
                {
                    continue;
                }

                if (found != 2 || !LinkNode(nodes[crossing[0]], crossing[1]) || !LinkNode(nodes[crossing[1]], crossing[0]))
                {
                    return null;
                }
            }

            foreach (MeshNode node in nodes.Values)
            {
                if (node.Links != 2 && node.Links != 0)
                {
                    return null;
                }
            }

            GraphicsPath area = new GraphicsPath(FillMode.Alternate);
            List<PointF> loop = new List<PointF>();
            foreach (KeyValuePair<long, MeshNode> start in nodes)
            {
                if (start.Value.Seen)
                {
                    continue;
                }

                loop.Clear();
                long previous = -1;
                long current = start.Key;
                while (true)
                {
                    MeshNode node = nodes[current];
                    if (node.Seen)
                    {
                        break;
                    }

                    node.Seen = true;
                    loop.Add(node.Point);
                    long next = node.A != previous ? node.A : node.B;
                    previous = current;
                    current = next;
                    if (current < 0)
                    {
                        break;
                    }
                }

                if (loop.Count >= 3)
                {
                    area.AddPolygon(loop.ToArray());
                }
            }

            return area;
        }

        static bool LinkNode(MeshNode node, long other)
        {
            if (node.Links >= 2)
            {
                return false;
            }

            if (node.Links++ == 0)
            {
                node.A = other;
            }
            else
            {
                node.B = other;
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Мелкие помощники рисования
        // ------------------------------------------------------------------

        static int AxisIndex(char axis)
        {
            return axis == 'x' ? 0 : axis == 'y' ? 1 : 2;
        }

        static void ViewAxes(bool axial, char axis, out int normal, out int u, out int v)
        {
            if (axial)
            {
                normal = 1; // плоскость y = 0 содержит ось z и ось x
                if (axis == 'z')
                {
                    u = 2;
                    v = 0;
                }
                else if (axis == 'x')
                {
                    u = 0;
                    v = 2;
                }
                else
                {
                    // Плоскость y = 0 поперечна оси y: разрез вдоль неё — плоскость z = 0.
                    normal = 2;
                    u = 1;
                    v = 0;
                }
            }
            else
            {
                normal = AxisIndex(axis);
                if (axis == 'z')
                {
                    u = 0;
                    v = 1;
                }
                else if (axis == 'x')
                {
                    u = 1;
                    v = 2;
                }
                else
                {
                    u = 0;
                    v = 2;
                }
            }
        }

        static double Coordinate(GdmlPoint point, int index)
        {
            return index == 0 ? point.X : index == 1 ? point.Y : point.Z;
        }

        static void AddProjected(List<PointF> points, ViewFrame frame, GdmlPoint point, int u, int v)
        {
            PointF screen = frame.ToScreen(Coordinate(point, u), Coordinate(point, v));
            foreach (PointF existing in points)
            {
                if (Math.Abs(existing.X - screen.X) < 0.01f && Math.Abs(existing.Y - screen.Y) < 0.01f)
                {
                    return;
                }
            }

            points.Add(screen);
        }

        /// <summary>Обход выпуклого сечения по углу вокруг центра. Вогнутый профиль так испортился бы: только коробке.</summary>
        static PointF[] SortedAroundCenter(List<PointF> points)
        {
            double cx = 0.0, cy = 0.0;
            foreach (PointF point in points)
            {
                cx += point.X;
                cy += point.Y;
            }

            cx /= Math.Max(1, points.Count);
            cy /= Math.Max(1, points.Count);

            List<SectionPoint> ordered = new List<SectionPoint>();
            foreach (PointF point in points)
            {
                ordered.Add(new SectionPoint
                {
                    Point = point,
                    Angle = Math.Atan2(point.Y - cy, point.X - cx),
                });
            }

            ordered.Sort((a, b) => a.Angle.CompareTo(b.Angle));
            PointF[] polygon = new PointF[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                polygon[i] = ordered[i].Point;
            }

            return polygon;
        }

        struct SectionPoint
        {
            public PointF Point;
            public double Angle;
        }

        /// <summary>
        /// Куда идёт сечение тела: залить с контуром, обвести или собрать в область для выреза.
        /// Геометрия одна на всех: вырез, собранный иначе, чем нарисован, разошёлся бы с телом.
        /// </summary>
        sealed class SectionSink
        {
            readonly Graphics graphics;
            readonly Brush brush;
            readonly Pen pen;
            readonly Region region;
            readonly GraphicsPath lines;

            /// <summary>Рёбра частей составного тела для контура по области.</summary>
            List<PointF[]> edges;

            /// <summary>
            /// ⛔ Сечение одного тела копится ОДНИМ путём и входит в область одним объединением. Цепочка
            /// `Region.Union` на каждый отрезок полиакона строит дерево глубиной в два числа плоскостей, и
            /// на 6000 плоскостей GDI+ переполняет стек в своём коде: это не исключение, заслон его не ловит.
            /// Части одного тела друг на друга не ложатся, поэтому ненулевое правило их не гасит.
            /// </summary>
            GraphicsPath pending;

            /// <summary>Сколько фигур дошло до рисования.</summary>
            public int Drawn;

            SectionSink(Graphics graphics, Brush brush, Pen pen, Region region, GraphicsPath lines)
            {
                this.graphics = graphics;
                this.brush = brush;
                this.pen = pen;
                this.region = region;
                this.lines = lines;
            }

            public static SectionSink ToFill(Graphics graphics, Brush brush, Pen pen)
            {
                return new SectionSink(graphics, brush, pen, null, null);
            }

            public static SectionSink ToOutline(Graphics graphics, Pen pen)
            {
                return new SectionSink(graphics, null, pen, null, null);
            }

            public static SectionSink ToRegion(Region region, GraphicsPath lines)
            {
                return new SectionSink(null, null, null, region, lines);
            }

            public static SectionSink ToEdges(List<PointF[]> edges)
            {
                return new SectionSink(null, null, null, null, null) { edges = edges };
            }

            /// <summary>Сетке есть что закрасить: рисуется заливка, а не собирается область.</summary>
            public bool WantsFill
            {
                get { return this.brush != null; }
            }

            public void FillOnly(GraphicsPath area)
            {
                this.graphics.FillPath(this.brush, area);
            }

            /// <summary>
            /// Сечение тела вращения: половины и признак шва на ребре. Заливка одним путём: две
            /// заливки встык дают шов сглаживанием и без всякого контура.
            /// </summary>
            public void Halves(PointF[] plus, PointF[] minus, bool[] seam)
            {
                if (plus == null || minus == null || this.region != null || this.edges != null)
                {
                    if (plus != null)
                    {
                        this.Polygon(plus);
                    }

                    if (minus != null)
                    {
                        this.Polygon(minus);
                    }

                    return;
                }

                if (plus.Length < 3)
                {
                    this.Drawn += plus.Length > 0 ? 1 : 0;
                    return;
                }

                if (this.brush != null)
                {
                    using (GraphicsPath area = new GraphicsPath(FillMode.Winding))
                    {
                        area.AddPolygon(plus);
                        area.AddPolygon(minus);
                        this.graphics.FillPath(this.brush, area);
                    }
                }

                using (GraphicsPath outline = new GraphicsPath())
                {
                    AddOpen(outline, plus, seam);
                    AddOpen(outline, minus, seam);
                    if (outline.PointCount > 0)
                    {
                        this.graphics.DrawPath(this.pen, outline);
                    }
                }

                this.Drawn++;
            }

            /// <summary>Контур многоугольника без рёбер шва; между разрывами — новая фигура.</summary>
            static void AddOpen(GraphicsPath path, PointF[] points, bool[] seam)
            {
                int count = points.Length;
                int first = Array.IndexOf(seam, true);
                if (first < 0)
                {
                    path.StartFigure();
                    path.AddPolygon(points);
                    return;
                }

                // Обход с ребра сразу за швом: иначе один непрерывный участок разрезался бы на два.
                bool open = false;
                for (int step = 1; step <= count; step++)
                {
                    int i = (first + step) % count;
                    if (seam[i])
                    {
                        open = false;
                        continue;
                    }

                    if (!open)
                    {
                        path.StartFigure();
                        open = true;
                    }

                    path.AddLine(points[i], points[(i + 1) % count]);
                }
            }

            public void Polygon(PointF[] points)
            {
                // Меньше точки экрана: на чертеже не видно, но плоскость тело задела.
                if (points.Length < 3)
                {
                    this.Drawn += points.Length > 0 ? 1 : 0;
                    return;
                }

                if (this.edges != null)
                {
                    this.edges.Add(points);
                    this.Drawn++;
                    return;
                }

                if (this.region != null)
                {
                    this.Pending().AddPolygon(points);
                    return;
                }

                if (this.brush != null)
                {
                    this.graphics.FillPolygon(this.brush, points);
                }

                this.graphics.DrawPolygon(this.pen, points);
                this.Drawn++;
            }

            public void Annulus(ViewFrame frame, PointF center, double rmin, double rmax)
            {
                if (rmax <= 0.0)
                {
                    return;
                }

                float outer = frame.ToPixels(rmax);
                float inner = frame.ToPixels(Math.Max(0.0, rmin));

                // Меньше половины точки: на чертеже не видно, но плоскость тело задела.
                if (outer < 0.5f)
                {
                    this.Drawn++;
                    return;
                }

                bool hasInner = inner > 0.5f;
                RectangleF outerRect = new RectangleF(center.X - outer, center.Y - outer, outer * 2.0f, outer * 2.0f);
                RectangleF innerRect = new RectangleF(center.X - inner, center.Y - inner, inner * 2.0f, inner * 2.0f);
                if (this.edges != null)
                {
                    this.edges.Add(Circle(center, outer));
                    if (hasInner)
                    {
                        this.edges.Add(Circle(center, inner));
                    }

                    this.Drawn++;
                    return;
                }

                if (this.region != null)
                {
                    GraphicsPath pendingPath = this.Pending();
                    pendingPath.AddEllipse(outerRect);
                    if (hasInner)
                    {
                        // Внутренний контур навстречу внешнему: при ненулевом правиле он вырезает, а не заливает.
                        using (GraphicsPath inner2 = new GraphicsPath())
                        {
                            inner2.AddEllipse(innerRect);
                            inner2.Reverse();
                            pendingPath.AddPath(inner2, false);
                        }
                    }

                    return;
                }

                if (this.brush != null)
                {
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.FillMode = FillMode.Alternate;
                        path.AddEllipse(outerRect);
                        if (hasInner)
                        {
                            path.AddEllipse(innerRect);
                        }

                        this.graphics.FillPath(this.brush, path);
                    }
                }

                this.graphics.DrawEllipse(this.pen, outerRect);
                if (hasInner)
                {
                    this.graphics.DrawEllipse(this.pen, innerRect);
                }

                this.Drawn++;
            }

            static PointF[] Circle(PointF center, float radius)
            {
                int steps = Math.Max(24, Math.Min(720, (int)(2.0 * Math.PI * radius / 3.0)));
                PointF[] points = new PointF[steps];
                for (int i = 0; i < steps; i++)
                {
                    double angle = 2.0 * Math.PI * i / steps;
                    points[i] = new PointF(center.X + radius * (float)Math.Cos(angle), center.Y + radius * (float)Math.Sin(angle));
                }

                return points;
            }

            GraphicsPath Pending()
            {
                if (this.pending == null)
                {
                    this.pending = new GraphicsPath(FillMode.Winding);
                }

                return this.pending;
            }

            /// <summary>Отдать накопленное сечение области: одно объединение на тело.</summary>
            public void Flush()
            {
                if (this.pending != null)
                {
                    this.region.Union(this.pending);
                    this.pending.Dispose();
                    this.pending = null;
                }
            }

            public void Lines(GraphicsPath path)
            {
                if (this.edges != null)
                {
                    return;
                }

                if (this.region != null)
                {
                    this.lines.AddPath(path, false);
                    return;
                }

                this.graphics.DrawPath(this.pen, path);
                this.Drawn++;
            }
        }

        /// <summary>
        /// (`AMBER322`) Какие детали рисовать бледнее: на «Файл GDML» — не вошедшие в поля редактора («не используется
        /// в геометрии»), в окне импорта — все, кроме выбранных (так выбранные подсвечены). null — все в полный цвет.
        /// Решения Amber 11.11.2026: «Файл GDML» + бледнить», «Маленький чертёж с подсветкой».
        /// </summary>
        public void SetPale(Func<GdmlVolumeInstance, bool> isPale)
        {
            this.pale = isPale;
            this.highlight = null;
            this.calloutPlaces.Clear();
            this.Invalidate();
        }

        Func<GdmlVolumeInstance, bool> pale;

        /// <summary>
        /// (`AMBER301`, критик 12.11.2026, п. 1.10) Предпросмотр выбора на вкладке, пока «Пересчитать» не нажата: выбранные
        /// детали в полном цвете, остальные бледнее — ТОЛЬКО цветом заливки. Слова чертежа (группы легенды «В расчёте» /
        /// «Не вошло в расчёт», выноски, стрелка «к пробе») по-прежнему говорят о применённой раскладке
        /// (<see cref="SetPale"/>, <see cref="SetMarks"/>): прежде предпросмотр ставил саму бледность, и легенда писала
        /// «Не вошло в расчёт» про защиту, отражатель и корпус, которые в расчёте есть. null — подсветки нет.
        /// <see cref="SetPale"/> и <see cref="SetGeometry"/> её снимают.
        /// </summary>
        public void SetHighlight(Func<GdmlVolumeInstance, bool> isDim)
        {
            this.highlight = isDim;
            this.Invalidate();
        }

        Func<GdmlVolumeInstance, bool> highlight;

        /// <summary>Доля холста в цвете бледной детали: деталь читается, но уходит на второй план.</summary>
        public const double PaleShare = 0.72;

        /// <summary>Цвет заливки детали: цвет вещества, у бледной — смешанный с холстом.</summary>
        internal Color FillOf(GdmlVolumeInstance volume)
        {
            Color color = this.ColorOf(volume.MaterialName);

            // Цвет заливки — по подсветке предпросмотра, если она есть; слова чертежа читают только `pale`.
            Func<GdmlVolumeInstance, bool> dim = this.highlight ?? this.pale;
            if (dim == null || !dim(volume))
            {
                return color;
            }

            return Fade(color);
        }

        /// <summary>Бледный цвет: смешан с холстом. Один на деталь чертежа и на значок легенды «Не вошло в расчёт».</summary>
        internal static Color Fade(Color color)
        {
            return Color.FromArgb(
                (int)Math.Round(color.R + (Canvas.R - color.R) * PaleShare),
                (int)Math.Round(color.G + (Canvas.G - color.G) * PaleShare),
                (int)Math.Round(color.B + (Canvas.B - color.B) * PaleShare));
        }

        /// <summary>Цвет вещества этой геометрии: у разных веществ одного файла цвета разные по построению.</summary>
        Color ColorOf(string materialName)
        {
            return this.Overview.ColorOf(materialName);
        }

        GdmlOverview Overview
        {
            get
            {
                if (this.overview == null)
                {
                    this.overview = GdmlOverview.Of(this.geometry);
                }

                return this.overview;
            }
        }

        Color ParentColor(GdmlVolumeInstance volume)
        {
            return volume != null && volume.Parent != null
                ? this.ColorOf(volume.Parent.MaterialName)
                : Canvas;
        }

        static Color Darken(Color color)
        {
            return Color.FromArgb(color.A, color.R * 2 / 3, color.G * 2 / 3, color.B * 2 / 3);
        }

        /// <summary>Длина метки: 1, 2 или 5 на степень десяти, не больше четверти ширины вида.</summary>
        static double ScaleLength(double width)
        {
            double quarter = width / 4.0;
            double decade = Math.Pow(10.0, Math.Floor(Math.Log10(quarter)));
            foreach (double factor in new[] { 5.0, 2.0, 1.0 })
            {
                if (factor * decade <= quarter)
                {
                    return factor * decade;
                }
            }

            return decade;
        }

        void DrawScale(Graphics graphics, ViewFrame frame, ViewSlot slot)
        {
            // От ширины вида, а не от размаха тела: тонкое и высокое тело давало метку в три точки.
            // При общем масштабе метка у видов одна: разные метки при равном масштабе путают.
            double plotWidth = slot.Plot.Width;
            if (this.commonScale > 0.0)
            {
                foreach (ViewSlot other in this.paintSlots)
                {
                    plotWidth = Math.Min(plotWidth, other.Plot.Width);
                }
            }

            double length = ScaleLength((plotWidth - 2.0 * FrameMargin) / frame.Scale);
            float pixels = frame.ToPixels(length);
            float x = slot.Panel.Left + 8.0f;
            float y = slot.Panel.Bottom - 10.0f;
            using (Pen pen = new Pen(Ink, 1.5f))
            {
                graphics.DrawLine(pen, x, y, x + pixels, y);
                graphics.DrawLine(pen, x, y - 4, x, y + 4);
                graphics.DrawLine(pen, x + pixels, y - 4, x + pixels, y + 4);
            }

            this.paintScales.Add(new KeyValuePair<RectangleF, double>(new RectangleF(x, y - 4, pixels, 8), length));
            string label = length >= 0.001
                ? GdmlText.N(length)
                : length.ToString("0.#E+0", CultureInfo.InvariantCulture);
            label += " " + GdmlText.T("GdmlUnitMm", "mm");
            SizeF size = graphics.MeasureString(label, this.Font);
            this.Mark(graphics, label, this.Font, InkBrush, x + pixels + 5.0f, y - size.Height / 2.0f, size);
        }

        /// <summary>
        /// Рамка вида: миллиметры → точки экрана, с сохранением пропорций. ⛔ Всё считается в
        /// double и зажимается перед выходом во float: размер в 1e39 мм во float — бесконечность,
        /// масштаб — ноль, а ноль на бесконечность — NaN, и GDI+ отвечает OverflowException
        /// (`AMBER292`).
        /// </summary>
        sealed class ViewFrame
        {
            /// <summary>Предел координаты экрана: дальше GDI+ рискует переполнением, а на чертеже разницы нет.</summary>
            const double ScreenLimit = 1e6;

            public readonly double UMin, UMax, VMin, VMax;
            public readonly double Scale;

            readonly double left;
            readonly double top;

            /// <summary>Сколько координат упёрлось в предел: фигура с такой вершиной уже не та.</summary>
            public long Clamped;

            ViewFrame(double umin, double umax, double vmin, double vmax, double scale, double left, double top)
            {
                this.UMin = umin;
                this.UMax = umax;
                this.VMin = vmin;
                this.VMax = vmax;
                this.Scale = scale;
                this.left = left;
                this.top = top;
            }

            /// <summary>null — масштаб не определён: диапазон не вмещается в double.</summary>
            public static ViewFrame Create(Rectangle rect, double umin, double umax, double vmin, double vmax, double margin, double scaleCap)
            {
                double du = umax - umin;
                double dv = vmax - vmin;
                double scale = Math.Min((rect.Width - 2.0 * margin) / du, (rect.Height - 2.0 * margin) / dv);

                // Общий масштаб двух видов: подгонка мелких расхождений округления мест не должна менять пропорции.
                if (scaleCap > 0.0)
                {
                    scale = Math.Min(scale, scaleCap);
                }

                double width = du * scale;
                double height = dv * scale;
                if (!IsFinite(scale) || scale <= 0.0 || !IsFinite(width) || !IsFinite(height))
                {
                    return null;
                }

                return new ViewFrame(umin, umax, vmin, vmax, scale,
                                     rect.Left + (rect.Width - width) / 2.0,
                                     rect.Top + (rect.Height - height) / 2.0);
            }

            float Clamp(double value)
            {
                if (Math.Abs(value) > ScreenLimit)
                {
                    this.Clamped++;
                }

                return (float)Math.Max(-ScreenLimit, Math.Min(ScreenLimit, value));
            }

            public PointF ToScreen(double u, double v)
            {
                return new PointF(
                    Clamp(this.left + (u - this.UMin) * this.Scale),
                    Clamp(this.top + (this.VMax - v) * this.Scale));
            }

            public float ToPixels(double millimetres)
            {
                return Clamp(millimetres * this.Scale);
            }

            /// <summary>Что занято телами на экране.</summary>
            public RectangleF Drawn
            {
                get
                {
                    return new RectangleF((float)this.left, (float)this.top,
                                          (float)((this.UMax - this.UMin) * this.Scale),
                                          (float)((this.VMax - this.VMin) * this.Scale));
                }
            }
        }
    }

    /// <summary>Что значит том для человека: на чертеже и в сводке одно и то же слово и один цвет.</summary>
    public enum GdmlRole
    {
        None,
        Crystal,
        Reflector,
        Housing,
        Sample,
        Vessel,
        Gas,
        Device,

        /// <summary>(`AMBER301`) Защита: роль не узнаётся по имени, её даёт раскладка лучами (<see cref="GdmlSketchMarks"/>).</summary>
        Shield,
    }

    /// <summary>
    /// (`AMBER301`, дизайнер 11.11.2026) Что из файла взято в расчёт — для чертежа: детали под выноски «Кристалл», «Проба»,
    /// «Защита» (те же три роли, что в блоке «Сопоставление»), вещества защиты для легенды, сторона «к пробе» и плашка
    /// отказа. Собирается из раскладки <see cref="GdmlFieldMap"/> одним вызовом, чтобы окно импорта и вкладка
    /// «Файл GDML» говорили одно.
    /// </summary>
    public sealed class GdmlSketchMarks
    {
        public GdmlVolumeInstance Crystal, Sample;

        /// <summary>Самая массивная деталь защиты: в неё показывает выноска «Защита».</summary>
        public GdmlVolumeInstance Shield;

        public readonly HashSet<string> ShieldMaterials = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Габарит деталей детектора, взятых в расчёт (кристалл, отражатель, корпус), по мировым осям: у его грани стоит
        /// стрелка «к пробе». null — детектор не взят.
        /// </summary>
        public double[] DetectorMin, DetectorMax;

        /// <summary>Мировая ось стороны «к пробе» (0, 1, 2) и её знак; −1 — стрелки нет (проба взята из файла).</summary>
        public int SampleAxis = -1;

        public int SampleSign;

        /// <summary>Плашка поверх чертежа: файл в расчёт не взят и почему; null — взят.</summary>
        public string Banner;

        /// <summary>
        /// Метки по раскладке. <paramref name="parts"/> — что берётся из файла; <paramref name="sideFacing"/> — в списке
        /// «Сторона к пробе» выбран бок (у бруска), иначе передний торец.
        /// </summary>
        public static GdmlSketchMarks From(GdmlGeometry geometry, GdmlFieldMap map, GdmlParts parts, bool sideFacing)
        {
            GdmlSketchMarks marks = new GdmlSketchMarks();
            if (map == null || (map.Found & GdmlParts.Detector) == 0 || (parts & GdmlParts.Detector) == 0)
            {
                marks.Banner = map == null || map.Crystal == null
                    ? GdmlText.T("GdmlSketchNoCrystal", "Crystal not recognised. Pick the part in the “Detector, crystal” list.")
                    : GdmlOverview.TakenText(map, GdmlParts.None);
                return marks;
            }

            parts &= map.Found;
            marks.Crystal = map.Crystal;
            if (geometry != null)
            {
                foreach (GdmlVolumeInstance volume in geometry.Volumes)
                {
                    if (volume == null || volume.Solid == null || !volume.BoundsKnown || !map.IsUsed(volume, GdmlParts.Detector))
                    {
                        continue;
                    }

                    if (marks.DetectorMin == null)
                    {
                        marks.DetectorMin = (double[])volume.Min.Clone();
                        marks.DetectorMax = (double[])volume.Max.Clone();
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        marks.DetectorMin[i] = Math.Min(marks.DetectorMin[i], volume.Min[i]);
                        marks.DetectorMax[i] = Math.Max(marks.DetectorMax[i], volume.Max[i]);
                    }
                }
            }
            if ((parts & GdmlParts.Sample) != 0)
            {
                marks.Sample = map.Sample;
            }
            else
            {
                // «+Z», «−X»: имя мировой оси стороны со знаком, как его пишет раскладка в сводке.
                string name = sideFacing ? map.SideName : map.FrontName;
                if (!string.IsNullOrEmpty(name) && name.Length == 2)
                {
                    marks.SampleAxis = "XYZ".IndexOf(name[1]);
                    marks.SampleSign = name[0] == '+' ? 1 : -1;
                }
            }

            if ((parts & GdmlParts.Shield) != 0 && geometry != null)
            {
                double best = -1.0;
                foreach (GdmlVolumeInstance volume in geometry.Volumes)
                {
                    if (volume == null || volume.Solid == null || !map.IsUsed(volume, GdmlParts.Shield)
                        || map.IsUsed(volume, GdmlParts.Detector | GdmlParts.Sample))
                    {
                        continue;
                    }

                    marks.ShieldMaterials.Add(volume.MaterialName ?? "");
                    double mass = double.IsNaN(volume.MassG) ? 0.0 : volume.MassG;
                    if (mass > best)
                    {
                        best = mass;
                        marks.Shield = volume;
                    }
                }
            }

            return marks;
        }
    }

    /// <summary>Вещество в легенде и в сводке.</summary>
    public sealed class GdmlMaterialRow
    {
        public string Name = "";
        public Color Color;
        public GdmlRole Role;
        public int Volumes;
        public double MassG;

        /// <summary>Масса части томов не известна: число — нижняя граница.</summary>
        public bool MassPartly;

        /// <summary>
        /// (fixGdml2, третий заход) Вещество есть в копиях, которые читатель не разместил (`&lt;paramvol&gt;`, `&lt;replicavol&gt;`
        /// вдоль rho/phi…): причина, пусто — всё учтено. Масса такой строки — нижняя граница или «?».
        /// </summary>
        public string Uncounted = "";

        public GdmlMaterial Material;

        /// <summary>Имя без служебной приставки Geant4: `G4_Al` человеку — алюминий, а не файл.</summary>
        public string Display
        {
            get
            {
                if (this.Name.Length == 0)
                {
                    return "?";
                }

                return this.Name.StartsWith("G4_", StringComparison.Ordinal) ? this.Name.Substring(3) : this.Name;
            }
        }

        public string MassText()
        {
            string unit = GdmlText.T("GdmlUnitG", "g");
            if (this.MassPartly && this.MassG <= 0.0)
            {
                return "? " + unit;
            }

            return (this.MassPartly ? "≥ " : "") + GdmlSketch.Fmt(this.MassG) + " " + unit;
        }

        public string LegendText()
        {
            string role = this.Role == GdmlRole.None ? "" : " (" + GdmlOverview.RoleName(this.Role).ToLowerInvariant() + ")";
            return this.Display + role + ": " + this.MassText() + this.UncountedText();
        }

        /// <summary>Хвост «, не учтено: &lt;paramvol&gt;» у вещества непрочитанных копий; пусто — всё учтено.</summary>
        public string UncountedText()
        {
            return this.Uncounted.Length == 0 ? "" : string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlMaterialUncounted", ", not counted: {0}"), this.Uncounted);
        }
    }

    /// <summary>
    /// Кто есть кто в импортированной геометрии (`AMBER301`). Роль узнаётся по имени тома и по составу, как
    /// кристалл у читателя: не узнано — роли нет и выноски нет, чертёж ничего не приписывает. Цвета — язык чертежа
    /// редактора <see cref="GeometrySketch"/>: те же кристалл, отражатель, корпус, зазор, стенка и проба.
    /// Остальные вещества берут цвет по порядку, поэтому в одном файле два вещества одним цветом не красятся.
    /// </summary>
    public sealed class GdmlOverview
    {
        static readonly Color CrystalColor = Color.FromArgb(0x35, 0xA5, 0xAD);
        static readonly Color ReflectorColor = Color.FromArgb(0x8C, 0xEC, 0xEC);
        static readonly Color HousingColor = Color.FromArgb(0x82, 0x90, 0xB0);
        static readonly Color GasColor = Color.FromArgb(0xE8, 0xEE, 0xF6);

        /// <summary>Оттенки роли «газ»: первый — прежний цвет газа, дальше бледные холодные, различимые на холсте и между собой.</summary>
        /// (четвёртый заход, по критику `critGdml2`) Попарно ΔE*ab не меньше 9.3 (прежние — 5.7), от холста не меньше 11.8;
        /// все холодные, кроме прежнего первого.
        static readonly Color[] GasShades =
        {
            GasColor,
            Color.FromArgb(0xB9, 0xCF, 0xEA),
            Color.FromArgb(0xE3, 0xD3, 0xF2),
            Color.FromArgb(0xC8, 0xED, 0xE2),
            Color.FromArgb(0xA9, 0xB6, 0xCC),
            Color.FromArgb(0xC4, 0xD6, 0xD9),
        };
        static readonly Color VesselColor = Color.FromArgb(0xA6, 0xD5, 0xE8);
        static readonly Color SampleColor = Color.FromArgb(0x78, 0x80, 0x8E);

        /// <summary>Прочие вещества: ни один не совпадает с цветами ролей и различим на холсте чертежа.</summary>
        static readonly Color[] OtherColors =
        {
            Color.FromArgb(0xD9, 0xB3, 0x6C),
            Color.FromArgb(0xC9, 0x7B, 0x7B),
            Color.FromArgb(0x9B, 0xBF, 0x7A),
            Color.FromArgb(0xB0, 0x8C, 0xC9),
            Color.FromArgb(0x5F, 0x8F, 0xC9),
            Color.FromArgb(0xD9, 0xA6, 0xC4),
            Color.FromArgb(0xE0, 0x8F, 0x55),
            Color.FromArgb(0xC4, 0xC4, 0x60),
            Color.FromArgb(0x5F, 0xB8, 0x94),
            Color.FromArgb(0x8E, 0x8E, 0xD8),
            Color.FromArgb(0xA8, 0x6F, 0x4F),
            Color.FromArgb(0xD8, 0x7F, 0x98),
        };

        /// <summary>Предел тела, которое может выпасть из рамки, мм: сто метров. Меньше — это ещё сцена.</summary>
        const double TrimAtLeast = 1e5;

        /// <summary>Во сколько раз тело должно быть больше следующего, чтобы рамка его не брала.</summary>
        const double TrimGap = 1000.0;

        /// <summary>Предок кристалла крупнее этого — уже не прибор, а окружение.</summary>
        const double MaxDeviceToCrystal = 20.0;

        /// <summary>Тело вокруг кристалла без предка в дереве: шире этого — уже не корпус (домик, сосуд).</summary>
        const double MaxHousingToCrystal = 2.0;

        /// <summary>Зазор отражателя от кристалла, в его поперечниках.</summary>
        const double MaxShellStep = 0.25;

        /// <summary>
        /// Наибольший просвет между оболочкой столба и тем, что внутри неё, мм на сторону. Вещество и имя уже отсеяли
        /// пластиковые сосуды, медь, свинец и защиту, так что предел нужен только против металлического сосуда или домика
        /// рядом с корпусом. Нижняя граница — 3.0 мм: корпус над банкой с пустым слоем воздуха 1.6, 2.0 и 3.0 мм остаётся
        /// корпусом (`GdmlSketchProbeA304`, при 1.5 мм прибор выходил по банке, 71.3 мм вместо 78.3). Верхняя — 5.0 мм:
        /// алюминиевая труба на 5 мм от корпуса корпусом стать не должна. 4.0 — середина. В настоящих файлах просвет 0
        /// (`gamma1s_*`: MgO → Al → резина → Al).
        /// </summary>
        const double MaxShellGapMm = 4.0;

        /// <summary>Сколько томов сводка называет поимённо; остальные — суммой.</summary>
        const int NamedVolumes = 8;

        /// <summary>Сколько неизмеренных томов сводка называет поимённо.</summary>
        const int NamedUnclear = 200;

        readonly GdmlGeometry geometry;
        readonly Dictionary<string, GdmlMaterialRow> rows = new Dictionary<string, GdmlMaterialRow>(StringComparer.Ordinal);
        readonly HashSet<GdmlVolumeInstance> outsize = new HashSet<GdmlVolumeInstance>();
        readonly HashSet<GdmlVolumeInstance> envelopes = new HashSet<GdmlVolumeInstance>();
        readonly List<GdmlVolumeInstance> envelopeOrder = new List<GdmlVolumeInstance>();
        GdmlVolumeInstance deviceRoot;

        public readonly List<GdmlMaterialRow> Materials = new List<GdmlMaterialRow>();
        public GdmlVolumeInstance Crystal, Sample, Vessel, DeviceBody;
        public double[] DeviceMin, DeviceMax;

        /// <summary>Габарит сцены без оболочек и без тел за рамкой; null — измерить нечего.</summary>
        public double[] SceneMin, SceneMax;

        GdmlOverview(GdmlGeometry geometry)
        {
            this.geometry = geometry;
        }

        public static GdmlOverview Of(GdmlGeometry geometry)
        {
            GdmlOverview o = new GdmlOverview(geometry);
            if (geometry != null)
            {
                o.FindOutsize();
                o.FindRoles();
                o.FindEnvelopes();
                o.FillSceneExtent();
                o.BuildMaterials();
            }

            return o;
        }

        public bool IsTrimmed(GdmlVolumeInstance volume)
        {
            return this.outsize.Count > 0 && this.outsize.Contains(volume);
        }

        /// <summary>Оболочка вокруг всей сцены (<see cref="FindEnvelopes"/>): не рисуется и рамку не задаёт.</summary>
        public bool IsEnvelope(GdmlVolumeInstance volume)
        {
            return this.envelopes.Count > 0 && this.envelopes.Contains(volume);
        }

        /// <summary>Том не задаёт рамку чертежа и габарит сцены: больше всего в тысячи раз или оболочка вокруг всего.</summary>
        public bool IsOutOfFrame(GdmlVolumeInstance volume)
        {
            return this.IsTrimmed(volume) || this.IsEnvelope(volume);
        }

        /// <summary>Оболочки по порядку, от внешней: имена для сводки.</summary>
        public IList<GdmlVolumeInstance> Envelopes
        {
            get { return this.envelopeOrder.AsReadOnly(); }
        }

        /// <summary>
        /// (`AMBER301`, fixGdml2 07.11.2026) Оболочка вокруг всей сцены — копия мира. `lht.gdml` (пример Geant4) кладёт всё в
        /// `vWorldVisible`: тот же короб 3000 × 500 × 3000 мм воздуха, что и мир, а прибор в нём — около 330 × 300 × 570 мм.
        /// Чертёж кадрировался по коробу, и прибор занимал пятую часть вида (снимок проверки экраном 07.11.2026).
        ///
        /// Оболочка — единственный верхний том среди измеренных (все прочие измеренные — его потомки) с измеренными
        /// потомками, из газа или пустоты (<see cref="IsGas"/>: та же мерка, что у ролей; `G4_Galactic` с 07.11.2026 есть в
        /// таблице G4, ρ = 1e-25). Сходит слоями: мир, видимый мир, зал — до восьми.
        ///
        /// (П1в по критику `critGdml2`) Оболочкой НЕ бывает том с ролью: воздух пробы (радон в воздухе, `f2_air_sample`) —
        /// по словам пробы, сосуда и защиты в имени; зазор у кристалла (`f1_airgap_top`, второй слой `f5_env_gap`) — прямой
        /// предок кристалла или том, обнимающий его (<see cref="HugsCrystal"/>); предок прибора и сам прибор (`FindDevice`:
        /// воздушный `RC103_device_log`, банка `G4_Galactic` с кристаллом `f3_vacuum_container`). Поэтому роли ищутся ДО
        /// оболочек, и одна мерка «газ» служит обоим. И оболочка обязана раздувать рамку: хотя бы по одной оси она больше
        /// своего содержимого на четверть (`w05_almost_fill_over2`: воздух 10 мм вокруг железа 10.00000001 мм — нет). Тесную
        /// оболочку прятать незачем: рамку она не меняет, а скрыть слой — потерять сведение.
        /// </summary>
        void FindEnvelopes()
        {
            List<GdmlVolumeInstance> measured = new List<GdmlVolumeInstance>();
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume != null && volume.Solid != null && volume.BoundsKnown && !this.IsTrimmed(volume))
                {
                    measured.Add(volume);
                }
            }

            HashSet<GdmlVolumeInstance> remaining = new HashSet<GdmlVolumeInstance>(measured);
            for (int layer = 0; layer < 8 && remaining.Count > 1; layer++)
            {
                GdmlVolumeInstance top = null;
                int tops = 0;
                foreach (GdmlVolumeInstance volume in measured)
                {
                    if (!remaining.Contains(volume))
                    {
                        continue;
                    }

                    bool under = false;
                    for (GdmlVolumeInstance up = volume.Parent; up != null && !under; up = up.Parent)
                    {
                        under = remaining.Contains(up);
                    }

                    if (!under)
                    {
                        top = volume;
                        if (++tops > 1)
                        {
                            break;
                        }
                    }
                }

                if (tops != 1 || top.Children == 0 || !IsGas(top.Material) || this.HasRole(top))
                {
                    return;
                }

                remaining.Remove(top);
                if (!this.WidensFrame(top, remaining))
                {
                    return;
                }

                this.envelopes.Add(top);
                this.envelopeOrder.Add(top);
            }
        }

        /// <summary>Том значит что-то сам: кристалл, прибор или его предок, проба, сосуд, защита, зазор у кристалла.</summary>
        bool HasRole(GdmlVolumeInstance volume)
        {
            if (ReferenceEquals(volume, this.Crystal) || ReferenceEquals(volume, this.deviceRoot) || ReferenceEquals(volume, this.DeviceBody)
                || ReferenceEquals(volume, this.Sample) || ReferenceEquals(volume, this.Vessel) || this.InsideDevice(volume))
            {
                return true;
            }

            if (NameHasWord(volume, GdmlGeometry.SampleWords) || NameHasWord(volume, GdmlGeometry.VesselWords)
                || NameHasWord(volume, GdmlGeometry.ShieldWords))
            {
                return true;
            }

            return this.Crystal != null && (ReferenceEquals(this.Crystal.Parent, volume) || this.HugsCrystal(volume));
        }

        /// <summary>
        /// Том обнимает кристалл: вмещает его с просветом не больше доли поперечника кристалла с каждой стороны — зазор или
        /// обвязка. (<see cref="TouchesCrystal"/> тут не годится: она говорит «не дальше», и любой объемлющий том ей «касается».)
        /// </summary>
        bool HugsCrystal(GdmlVolumeInstance volume)
        {
            if (this.Crystal == null || !this.Crystal.BoundsKnown || !volume.BoundsKnown || ReferenceEquals(volume, this.Crystal))
            {
                return false;
            }

            double room = MaxShellStep * this.Across(this.Crystal);
            for (int i = 0; i < 3; i++)
            {
                double below = this.Crystal.Min[i] - volume.Min[i], above = volume.Max[i] - this.Crystal.Max[i];
                if (below < -1e-9 || above < -1e-9 || below > room || above > room)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Габарит измеренных потомков каждого предка кристалла: [xmin, ymin, zmin, xmax, ymax, zmax]. Один проход.</summary>
        Dictionary<GdmlVolumeInstance, double[]> AncestorContent()
        {
            HashSet<GdmlVolumeInstance> chain = new HashSet<GdmlVolumeInstance>();
            for (GdmlVolumeInstance up = this.Crystal.Parent; up != null; up = up.Parent)
            {
                chain.Add(up);
            }

            Dictionary<GdmlVolumeInstance, double[]> content = new Dictionary<GdmlVolumeInstance, double[]>();
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null || !volume.BoundsKnown || this.IsTrimmed(volume))
                {
                    continue;
                }

                for (GdmlVolumeInstance up = volume.Parent; up != null; up = up.Parent)
                {
                    if (!chain.Contains(up))
                    {
                        continue;
                    }

                    double[] box;
                    if (!content.TryGetValue(up, out box))
                    {
                        box = new[] { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue };
                        content[up] = box;
                    }

                    for (int i = 0; i < 3; i++)
                    {
                        box[i] = Math.Min(box[i], volume.Min[i]);
                        box[i + 3] = Math.Max(box[i + 3], volume.Max[i]);
                    }
                }
            }

            return content;
        }

        /// <summary>Том больше своего содержимого хотя бы на четверть по какой-то оси — та же мерка, что у оболочки.</summary>
        static bool Roomy(GdmlVolumeInstance volume, double[] inner)
        {
            for (int i = 0; i < 3; i++)
            {
                if (volume.Max[i] - volume.Min[i] > 1.25 * (inner[i + 3] - inner[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Оболочка больше своего содержимого хотя бы на четверть по какой-то оси: без неё рамка заметно теснее.</summary>
        bool WidensFrame(GdmlVolumeInstance envelope, HashSet<GdmlVolumeInstance> content)
        {
            double[] min = null, max = null;
            foreach (GdmlVolumeInstance volume in content)
            {
                if (min == null)
                {
                    min = (double[])volume.Min.Clone();
                    max = (double[])volume.Max.Clone();
                }

                for (int i = 0; i < 3; i++)
                {
                    min[i] = Math.Min(min[i], volume.Min[i]);
                    max[i] = Math.Max(max[i], volume.Max[i]);
                }
            }

            if (min == null)
            {
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                if (envelope.Max[i] - envelope.Min[i] > 1.25 * (max[i] - min[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Габарит сцены: все измеренные тома, кроме тех, что не задают рамку чертежа. Тот же, что у рамки чертежа.</summary>
        void FillSceneExtent()
        {
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null || !volume.BoundsKnown || this.IsOutOfFrame(volume))
                {
                    continue;
                }

                if (this.SceneMin == null)
                {
                    this.SceneMin = (double[])volume.Min.Clone();
                    this.SceneMax = (double[])volume.Max.Clone();
                }

                for (int i = 0; i < 3; i++)
                {
                    this.SceneMin[i] = Math.Min(this.SceneMin[i], volume.Min[i]);
                    this.SceneMax[i] = Math.Max(this.SceneMax[i], volume.Max[i]);
                }
            }
        }

        public Color ColorOf(string materialName)
        {
            GdmlMaterialRow row;
            return materialName != null && this.rows.TryGetValue(materialName, out row)
                ? row.Color
                : Color.FromArgb(0x90, 0x90, 0x90);
        }

        public static string RoleName(GdmlRole role)
        {
            switch (role)
            {
                case GdmlRole.Crystal:
                    return GdmlText.T("GdmlRoleCrystal", "Crystal");
                case GdmlRole.Reflector:
                    return GdmlText.T("GdmlRoleReflector", "Reflector");
                case GdmlRole.Housing:
                    return GdmlText.T("GdmlRoleHousing", "Housing");
                case GdmlRole.Sample:
                    return GdmlText.T("GdmlRoleSample", "Sample");
                case GdmlRole.Vessel:
                    return GdmlText.T("GdmlRoleVessel", "Vessel");
                case GdmlRole.Gas:
                    return GdmlText.T("GdmlRoleGas", "Gas");
                case GdmlRole.Device:
                    // Решение Amber 11.11.2026 вопросником, дословно: ««Детектор» (Рекомендую)» — то же слово, что в блоке
                    // «Сопоставление» и в строке итога; «Прибор» читался как «всё, что принёс человек».
                    return GdmlText.T("GdmlRoleDevice", "Detector");
                case GdmlRole.Shield:
                    return GdmlText.T("GdmlRoleShield", "Shield");
                default:
                    return "";
            }
        }

        static bool NameHasWord(GdmlVolumeInstance volume, string[] words)
        {
            return GdmlGeometry.NameHasWord(volume.VolumeName, words);
        }

        static bool NameHas(GdmlVolumeInstance volume, params string[] parts)
        {
            string name = volume.VolumeName ?? "";
            foreach (string part in parts)
            {
                if (name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        static double Span(GdmlVolumeInstance volume)
        {
            return Math.Max(volume.Max[0] - volume.Min[0], Math.Max(volume.Max[1] - volume.Min[1], volume.Max[2] - volume.Min[2]));
        }

        /// <summary>
        /// Деталь в 1e300 мм рядом с прибором в 100 мм: рамка по ней сводила прибор в точку. Такое тело не
        /// рисуется, и подпись вида его называет; тело, которое просто велико, рамку задаёт, как и прежде.
        /// </summary>
        void FindOutsize()
        {
            List<GdmlVolumeInstance> sized = new List<GdmlVolumeInstance>();
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume != null && volume.BoundsKnown && !double.IsNaN(Span(volume)) && !double.IsInfinity(Span(volume)))
                {
                    sized.Add(volume);
                }
            }

            sized.Sort((a, b) => Span(b).CompareTo(Span(a)));
            for (int i = 0; i + 1 < sized.Count; i++)
            {
                double next = Math.Max(Span(sized[i + 1]), 1e-9);
                if (Span(sized[i]) >= TrimAtLeast && Span(sized[i]) >= TrimGap * next)
                {
                    for (int k = 0; k <= i; k++)
                    {
                        this.outsize.Add(sized[k]);
                    }
                }
            }
        }

        static bool IsGas(GdmlMaterial material)
        {
            return material != null && material.HasDensity && material.Density < 0.01;
        }

        /// <summary>Сцинтиллятор или полупроводник по составу: когда том не назван кристаллом.</summary>
        void FindRoles()
        {
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null || volume.Solid == null)
                {
                    continue;
                }

                if (this.Crystal == null && this.geometry.CrystalName.Length > 0 && volume.VolumeName == this.geometry.CrystalName)
                {
                    this.Crystal = volume;
                }
            }

            // Признак вещества один на чтение и роли (`GdmlGeometry.LargestDetectorVolume`).
            this.Crystal = this.Crystal ?? this.geometry.LargestDetectorVolume();
            this.FindDevice();

            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null || volume.Solid == null || IsGas(volume.Material)
                    || ReferenceEquals(volume, this.Crystal) || this.InsideDevice(volume))
                {
                    continue;
                }

                if (NameHasWord(volume, GdmlGeometry.SampleWords)
                    && (this.Sample == null || volume.VolumeCm3 > this.Sample.VolumeCm3))
                {
                    this.Sample = volume;
                }
                else if (NameHasWord(volume, GdmlGeometry.VesselWords)
                         && (this.Vessel == null || volume.VolumeCm3 > this.Vessel.VolumeCm3))
                {
                    this.Vessel = volume;
                }
            }

            this.FillDeviceExtent();
        }

        /// <summary>
        /// Прибор — то, что РЕАЛЬНО содержит кристалл, и ничего больше: самое большое тело файла прибором не
        /// становится. По дереву: верхний предок кристалла под миром, не больше чем в 20 раз крупнее кристалла
        /// (RC-103 — в 12; зал вокруг прибора — нет). Без предков (все тома в мире, GS2020): самое тесное тело,
        /// чей габарит вмещает кристалл и не вдвое шире него (свинцовый домик в шесть раз, стенка маринелли
        /// и не вмещает). Ничего такого нет — прибора нет: не узнано, значит роли нет.
        /// </summary>
        void FindDevice()
        {
            if (this.Crystal == null || !this.Crystal.BoundsKnown)
            {
                return;
            }

            double crystalSpan = Math.Max(Span(this.Crystal), 1e-9);
            Dictionary<GdmlVolumeInstance, double[]> content = this.AncestorContent();
            for (GdmlVolumeInstance up = this.Crystal.Parent; up != null && up.Parent != null; up = up.Parent)
            {
                // (fixGdml2, третий заход) Просторный газ — уже зал или мир, а не прибор: выше него не подниматься. У `lht.gdml`
                // кристаллы CsI 6 × 6 × 30 см (с третьего захода копии репликации размещены), и мир 3000 мм — всего в 10 раз
                // больше кристалла: без этой ступени предком прибора становился `vWorldVisible`, и оболочки не было.
                // Воздушный `RC103_device_log` тесен вокруг своих частей и остаётся предком прибора.
                double[] inner;
                if (up.BoundsKnown && IsGas(up.Material) && content.TryGetValue(up, out inner) && Roomy(up, inner))
                {
                    break;
                }

                if (up.BoundsKnown && Span(up) <= MaxDeviceToCrystal * crystalSpan)
                {
                    this.deviceRoot = up;
                }
            }

            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null || volume.Solid == null || !volume.BoundsKnown || this.IsTrimmed(volume)
                    || ReferenceEquals(volume, this.Crystal) || IsGas(volume.Material) || this.IsReflector(volume)
                    || GdmlGeometry.LooksLikeDetector(volume.Material))
                {
                    continue;
                }

                if (this.deviceRoot != null)
                {
                    // Корпус под предком: самая большая часть, в неё показывает выноска. Не из вещества детектора (третий заход
                    // fixGdml2): в сборке из 24 кристаллов CsI `lht.gdml` «корпусом» становился соседний кристалл.
                    if (IsUnder(volume, this.deviceRoot) && volume.VolumeCm3 > 0.0
                        && (this.DeviceBody == null || volume.VolumeCm3 > this.DeviceBody.VolumeCm3))
                    {
                        this.DeviceBody = volume;
                    }
                }
            }

            if (this.deviceRoot == null)
            {
                this.DeviceBody = this.OutermostShell();
            }
        }

        /// <summary>
        /// (`AMBER317`) Все тома в мире: корпус — наружная оболочка столба вокруг кристалла. Оболочки идут от кристалла наружу, каждая
        /// вмещает его и прилегает к тому, что внутри, без просвета больше тонкого слоя: столб прибора плотный, а за ним зазор — и там
        /// уже колодец сосуда или домик. По длине запрета нет: корпус с ФЭУ много длиннее кристалла. Шире двух кристаллов не прибор.
        /// </summary>
        GdmlVolumeInstance OutermostShell()
        {
            double crystalAcross = Math.Max(this.Across(this.Crystal), 1e-9);
            List<GdmlVolumeInstance> shells = new List<GdmlVolumeInstance>();
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume != null && volume.Solid != null && volume.BoundsKnown && !this.IsTrimmed(volume)
                    && !ReferenceEquals(volume, this.Crystal) && Encloses(volume, this.Crystal))
                {
                    shells.Add(volume);
                }
            }

            shells.Sort((a, b) => this.Across(a).CompareTo(this.Across(b)));
            GdmlVolumeInstance body = null;
            foreach (GdmlVolumeInstance shell in shells)
            {
                double across = this.Across(shell);

                // Проба, сосуд и защита в столб не входят и его обрывают: за ними уже не прибор.
                if (across > MaxHousingToCrystal * crystalAcross
                    || NameHasWord(shell, GdmlGeometry.SampleWords) || NameHasWord(shell, GdmlGeometry.VesselWords)
                    || NameHasWord(shell, GdmlGeometry.ShieldWords))
                {
                    break;
                }

                // Оболочка прилегает к столбу: просвет между ней и тем, что лежит внутри неё (оболочки, кольца отражателя,
                // окно), не больше тонкого слоя.
                if (this.InnerAcross(shell) - this.ContentAcross(shell) > 2.0 * MaxShellGapMm)
                {
                    break;
                }

                if (!IsGas(shell.Material) && !this.IsReflector(shell) && IsHousingMaterial(shell.Material))
                {
                    body = shell;
                }
            }

            return body;
        }

        /// <summary>Наибольший поперечник того, что лежит внутри оболочки и рядом с кристаллом по длине: столб, к которому она прилегает.</summary>
        double ContentAcross(GdmlVolumeInstance shell)
        {
            int along = this.geometry.Axis == 'x' ? 0 : this.geometry.Axis == 'y' ? 1 : 2;
            double content = this.Across(this.Crystal);
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null || ReferenceEquals(volume, shell) || !volume.BoundsKnown || IsGas(volume.Material) || !Encloses(shell, volume)
                    || volume.Max[along] < this.Crystal.Min[along] || volume.Min[along] > this.Crystal.Max[along])
                {
                    continue;
                }

                content = Math.Max(content, this.Across(volume));
            }

            return content;
        }

        /// <summary>
        /// Внутренний поперечник оболочки по её объёму: круглая — π(D² − d²)L/4, прямоугольная — (A² − a²)L. Формы стенки
        /// чтение не знает, а объём знает; тело без полости даёт ноль.
        /// </summary>
        double InnerAcross(GdmlVolumeInstance shell)
        {
            int along = this.geometry.Axis == 'x' ? 0 : this.geometry.Axis == 'y' ? 1 : 2;
            double length = shell.Max[along] - shell.Min[along];
            double outer = this.Across(shell);
            double volume = shell.VolumeCm3 * 1000.0;
            if (!(length > 0.0) || !(volume > 0.0))
            {
                return 0.0;
            }

            bool box = shell.Solid != null && shell.Solid.Kind == GdmlSolidKind.Box;
            double inner2 = box ? outer * outer - volume / length : outer * outer - 4.0 * volume / (Math.PI * length);
            return inner2 > 0.0 ? Math.Sqrt(inner2) : 0.0;
        }

        /// <summary>
        /// Вещество корпуса прибора: лёгкий металл или сталь (Al, Mg, Be, Ti, Fe основным элементом), пластик корпуса с N или O
        /// (ABS, поликарбонат, полиацеталь, акрил). Медь, кадмий, олово, свинец, вольфрам — слои защиты; чистые C-H (PE, PP,
        /// резина) — сосуды и прокладки; фторопласт — отражатель.
        /// </summary>
        static bool IsHousingMaterial(GdmlMaterial material)
        {
            if (material == null || material.Fractions.Count == 0)
            {
                return false;
            }

            int main = 0;
            double most = -1.0;
            bool organic = true;
            foreach (KeyValuePair<int, double> part in material.Fractions)
            {
                organic &= part.Key == 1 || part.Key == 6 || part.Key == 7 || part.Key == 8;
                if (part.Value > most)
                {
                    most = part.Value;
                    main = part.Key;
                }
            }

            if (organic)
            {
                return material.Fractions.ContainsKey(6) && (material.Fractions.ContainsKey(7) || material.Fractions.ContainsKey(8));
            }

            return main == 4 || main == 12 || main == 13 || main == 22 || main == 26;
        }

        /// <summary>Поперечник тома: больший из размеров поперёк оси прибора.</summary>
        double Across(GdmlVolumeInstance volume)
        {
            int along = this.geometry.Axis == 'x' ? 0 : this.geometry.Axis == 'y' ? 1 : 2;
            double across = 0.0;
            for (int i = 0; i < 3; i++)
            {
                if (i != along)
                {
                    across = Math.Max(across, volume.Max[i] - volume.Min[i]);
                }
            }

            return across;
        }

        static bool IsUnder(GdmlVolumeInstance volume, GdmlVolumeInstance root)
        {
            for (GdmlVolumeInstance up = volume; up != null; up = up.Parent)
            {
                if (ReferenceEquals(up, root))
                {
                    return true;
                }
            }

            return false;
        }

        static bool Encloses(GdmlVolumeInstance outer, GdmlVolumeInstance inner)
        {
            for (int i = 0; i < 3; i++)
            {
                double tolerance = 1e-6 * Math.Max(1.0, outer.Max[i] - outer.Min[i]);
                if (inner.Min[i] < outer.Min[i] - tolerance || inner.Max[i] > outer.Max[i] + tolerance)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Поперёк оси том не шире корпуса: узел того же столба, а не проба вокруг него.</summary>
        bool FitsAcrossHousing(GdmlVolumeInstance volume)
        {
            int along = this.geometry.Axis == 'x' ? 0 : this.geometry.Axis == 'y' ? 1 : 2;
            for (int i = 0; i < 3; i++)
            {
                double room = 0.01 * (this.DeviceBody.Max[i] - this.DeviceBody.Min[i]);
                if (i != along && (volume.Min[i] < this.DeviceBody.Min[i] - room || volume.Max[i] > this.DeviceBody.Max[i] + room))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Том — часть прибора: под его предком либо не шире корпуса и задевает его. Такой том пробой или сосудом
        /// не бывает, как бы ни назывался: «Am241_source» в корпусе — источник калибровки, а не проба.
        /// </summary>
        bool InsideDevice(GdmlVolumeInstance volume)
        {
            if (this.deviceRoot != null)
            {
                return IsUnder(volume, this.deviceRoot);
            }

            if (this.DeviceBody == null || !volume.BoundsKnown)
            {
                return false;
            }

            if (ReferenceEquals(volume, this.DeviceBody))
            {
                return true;
            }

            if (!this.FitsAcrossHousing(volume))
            {
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                if (volume.Max[i] <= this.DeviceBody.Min[i] || volume.Min[i] >= this.DeviceBody.Max[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Габарит прибора в подписи: предок целиком или все узлы столба, кроме пробы и сосуда с содержимым.</summary>
        void FillDeviceExtent()
        {
            if (this.DeviceBody == null)
            {
                return;
            }

            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null || !volume.BoundsKnown || this.IsTrimmed(volume))
                {
                    continue;
                }

                bool member = this.deviceRoot != null
                    ? ReferenceEquals(volume, this.deviceRoot) || IsUnder(volume, this.deviceRoot)
                    : this.FitsAcrossHousing(volume) && !IsUnder(volume, this.Sample) && !IsUnder(volume, this.Vessel);
                if (!member)
                {
                    continue;
                }

                if (this.DeviceMin == null)
                {
                    this.DeviceMin = (double[])volume.Min.Clone();
                    this.DeviceMax = (double[])volume.Max.Clone();
                }

                for (int i = 0; i < 3; i++)
                {
                    this.DeviceMin[i] = Math.Min(this.DeviceMin[i], volume.Min[i]);
                    this.DeviceMax[i] = Math.Max(this.DeviceMax[i], volume.Max[i]);
                }
            }
        }

        /// <summary>Отражатель: по имени, а без него по веществу и месту: порошок или плёнка отражателя, вплотную к кристаллу.</summary>
        bool IsReflector(GdmlVolumeInstance volume)
        {
            return NameHas(volume, "reflect", "esr", "teflon", "ptfe", "отраж")
                   || (IsReflectorMaterial(volume.Material) && this.TouchesCrystal(volume));
        }

        /// <summary>MgO, Al2O3, TiO2, BaSO4, PTFE: ровно эти элементы и ни одного другого.</summary>
        static bool IsReflectorMaterial(GdmlMaterial material)
        {
            if (material == null)
            {
                return false;
            }

            Func<int[], bool> exactly = zs => material.Fractions.Count == zs.Length && Array.TrueForAll(zs, z => material.Fractions.ContainsKey(z));
            return exactly(new[] { 12, 8 }) || exactly(new[] { 13, 8 }) || exactly(new[] { 22, 8 })
                   || exactly(new[] { 56, 16, 8 }) || exactly(new[] { 6, 9 });
        }

        /// <summary>Том рядом с кристаллом: зазор по каждой оси не больше доли его поперечника.</summary>
        bool TouchesCrystal(GdmlVolumeInstance volume)
        {
            if (this.Crystal == null || !this.Crystal.BoundsKnown || !volume.BoundsKnown || ReferenceEquals(volume, this.Crystal))
            {
                return false;
            }

            double room = MaxShellStep * this.Across(this.Crystal);
            for (int i = 0; i < 3; i++)
            {
                if (this.Crystal.Min[i] - volume.Max[i] > room || volume.Min[i] - this.Crystal.Max[i] > room)
                {
                    return false;
                }
            }

            return true;
        }

        internal GdmlRole RoleOf(GdmlVolumeInstance volume)
        {
            if (ReferenceEquals(volume, this.Crystal))
            {
                return GdmlRole.Crystal;
            }

            if (ReferenceEquals(volume, this.Sample))
            {
                return GdmlRole.Sample;
            }

            if (ReferenceEquals(volume, this.Vessel))
            {
                return GdmlRole.Vessel;
            }

            if (IsGas(volume.Material))
            {
                return GdmlRole.Gas;
            }

            if (this.IsReflector(volume))
            {
                return GdmlRole.Reflector;
            }

            return ReferenceEquals(volume, this.DeviceBody) ? GdmlRole.Housing : GdmlRole.None;
        }

        void BuildMaterials()
        {
            // Важность роли: у вещества одна роль, и кристалл важнее всего.
            GdmlRole[] order = { GdmlRole.Crystal, GdmlRole.Sample, GdmlRole.Vessel, GdmlRole.Reflector, GdmlRole.Housing, GdmlRole.Gas };
            foreach (GdmlVolumeInstance volume in this.geometry.Volumes)
            {
                if (volume == null)
                {
                    continue;
                }

                string name = volume.MaterialName ?? "";
                GdmlMaterialRow row;
                if (!this.rows.TryGetValue(name, out row))
                {
                    row = new GdmlMaterialRow { Name = name, Material = volume.Material };
                    this.rows[name] = row;
                    this.Materials.Add(row);
                }

                row.Volumes++;
                if (double.IsNaN(volume.MassG))
                {
                    row.MassPartly = true;
                }
                else
                {
                    row.MassG += volume.MassG;
                }

                GdmlRole role = this.RoleOf(volume);
                if (role != GdmlRole.None
                    && (row.Role == GdmlRole.None || Array.IndexOf(order, role) < Array.IndexOf(order, row.Role)))
                {
                    row.Role = role;
                }
            }

            // Вещества копий, которых читатель не разместил: строка есть, масса — нижняя граница или «?».
            foreach (KeyValuePair<string, string> uncounted in this.geometry.UncountedMaterials)
            {
                GdmlMaterialRow row;
                if (!this.rows.TryGetValue(uncounted.Key, out row))
                {
                    row = new GdmlMaterialRow { Name = uncounted.Key, Material = this.geometry.MaterialOf(uncounted.Key) };
                    this.rows[uncounted.Key] = row;
                    this.Materials.Add(row);
                }

                row.MassPartly = true;
                row.Uncounted = uncounted.Value;
            }

            int next = 0;
            int gases = 0;
            foreach (GdmlMaterialRow row in this.Materials)
            {
                switch (row.Role)
                {
                    case GdmlRole.Crystal:
                        row.Color = CrystalColor;
                        break;
                    case GdmlRole.Reflector:
                        row.Color = ReflectorColor;
                        break;
                    case GdmlRole.Housing:
                        row.Color = HousingColor;
                        break;
                    case GdmlRole.Sample:
                        row.Color = SampleColor;
                        break;
                    case GdmlRole.Vessel:
                        row.Color = VesselColor;
                        break;
                    case GdmlRole.Gas:
                        // (третий заход, решение Amber 07.11.2026 вопросником, дословно: «Оттенки газа (Рекомендую)») Роль
                        // «газ» — семейство бледных холодных цветов, у каждого газа свой оттенок: AIR и G4_Galactic у
                        // `lht.gdml` прежде красились одним цветом.
                        row.Color = GasShades[gases++ % GasShades.Length];
                        break;
                    default:
                        row.Color = OtherColor(next++);
                        break;
                }
            }

            this.Materials.Sort((a, b) => b.MassG.CompareTo(a.MassG));
        }

        /// <summary>Двенадцать цветов, дальше — шаг по кругу оттенков: ни одно вещество не повторит соседа цветом.</summary>
        static Color OtherColor(int index)
        {
            if (index < OtherColors.Length)
            {
                return OtherColors[index];
            }

            double hue = (index * 137.508) % 360.0;
            double light = 0.55 + 0.1 * (index % 3);
            double chroma = 0.45;
            double h = hue / 60.0;
            double x = chroma * (1.0 - Math.Abs(h % 2.0 - 1.0));
            double r = h < 1 ? chroma : h < 2 ? x : h < 3 ? 0 : h < 4 ? 0 : h < 5 ? x : chroma;
            double g = h < 1 ? x : h < 2 ? chroma : h < 3 ? chroma : h < 4 ? x : 0;
            double b = h < 2 ? 0 : h < 3 ? x : h < 4 ? chroma : h < 5 ? chroma : x;
            double m = light - chroma / 2.0;
            return Color.FromArgb((int)(255.0 * (r + m)), (int)(255.0 * (g + m)), (int)(255.0 * (b + m)));
        }

        /// <summary>Размер словами редактора: тело вращения — «⌀50.8 × 50.8 мм», прочее — три числа.</summary>
        public static string SizeText(GdmlVolumeInstance volume, char axis)
        {
            string unit = GdmlText.T("GdmlUnitMm", "mm");
            if (!volume.BoundsKnown)
            {
                return "? " + unit;
            }

            // (`AMBER301`, fixGdml2) Тело вращения полного оборота — «⌀ × длина» ЕГО СОБСТВЕННЫМИ размерами, а не габаритом в
            // осях мира. У повёрнутого тела габарит шире диаметра: `vTarget` из `lht.gdml` — tube rmax 5 см, z 10 см,
            // повёрнут `TargetRot` (x 90°, z −40°) — подписывался «⌀140.9 × 100 мм» (100·(cos 40° + sin 40°)) вместо ⌀100 × 100.
            // Поворот у GDML ортогонален (масштаб читатель сводит к знакам, `ToMirror`), поэтому свой размер тела и есть размер.
            GdmlSolid solid = volume.Solid;
            if (solid != null && solid.BoundsKnown
                && (solid.Kind == GdmlSolidKind.Polycone || solid.Kind == GdmlSolidKind.GenericPolycone)
                && solid.DeltaPhi >= 360.0)
            {
                double diameter = solid.Max[0] - solid.Min[0];
                double length = solid.Max[2] - solid.Min[2];
                if (diameter > 0.0 && length >= 0.0 && Math.Abs((solid.Max[1] - solid.Min[1]) - diameter) <= 1e-6 * diameter)
                {
                    return "⌀" + GdmlSketch.Fmt(diameter) + " × " + GdmlSketch.Fmt(length) + " " + unit;
                }
            }

            // (четвёртый заход, по критику `critGdml2`) Коробка — тоже своими размерами, x × y × z из файла, а не габаритом
            // в осях мира: кристалл CsI 6 × 6 × 30 см `lht.gdml`, повёрнутый на 65°, подписывался «297.2 × 60 × 181.2 мм».
            // Габарит в осях мира остаётся у рамки чертежа и у габарита прибора и сцены.
            if (solid != null && solid.Kind == GdmlSolidKind.Box && solid.BoundsKnown)
            {
                return GdmlSketch.Fmt(solid.SizeX) + " × " + GdmlSketch.Fmt(solid.SizeY) + " × " + GdmlSketch.Fmt(solid.SizeZ) + " " + unit;
            }

            return SizeText(volume.Min, volume.Max, axis, false);
        }

        static string SizeText(double[] min, double[] max, char axis, bool round)
        {
            string unit = GdmlText.T("GdmlUnitMm", "mm");
            double dx = max[0] - min[0], dy = max[1] - min[1], dz = max[2] - min[2];
            if (round)
            {
                int along = axis == 'x' ? 0 : axis == 'y' ? 1 : 2;
                double[] size = { dx, dy, dz };
                double a = size[(along + 1) % 3], b = size[(along + 2) % 3];
                if (Math.Abs(a - b) <= 1e-3 * Math.Max(a, b))
                {
                    return "⌀" + GdmlSketch.Fmt(a) + " × " + GdmlSketch.Fmt(size[along]) + " " + unit;
                }
            }

            return GdmlSketch.Fmt(dx) + " × " + GdmlSketch.Fmt(dy) + " × " + GdmlSketch.Fmt(dz) + " " + unit;
        }

        /// <summary>
        /// (`AMBER301`) Сборка детектора занимает всю сцену: её размеры уже стоят на размерных линиях габарита, выноска
        /// с теми же числами не рисуется (RC-110: «Прибор 126.6 × 34.1 × 21.7 мм» рядом с 126.6 и 21.7 на линиях).
        /// </summary>
        public bool DeviceFillsScene()
        {
            if (this.DeviceMin == null || this.SceneMin == null)
            {
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                double span = Math.Max(1e-9, this.SceneMax[i] - this.SceneMin[i]);
                if (Math.Abs(this.DeviceMin[i] - this.SceneMin[i]) > 1e-6 * span || Math.Abs(this.DeviceMax[i] - this.SceneMax[i]) > 1e-6 * span)
                {
                    return false;
                }
            }

            return true;
        }

        public string DeviceSizeText()
        {
            return this.DeviceMin == null ? "" : SizeText(this.DeviceMin, this.DeviceMax, this.geometry.Axis, false);
        }

        // ------------------------------------------------------------------
        // Сводка: заголовок, главное, вещества, крупные поимённо, мелочь суммой, что не измерено, замечания
        // ------------------------------------------------------------------

        enum Style
        {
            Title,
            Head,
            Normal,
            Dim,
            Strong,
        }

        struct Run
        {
            public string Text;
            public Style Style;
            public Color? Color;

            /// <summary>Поле знака (highlight в RTF): обводка бледного значка; null — без поля.</summary>
            public Color? Back;
        }

        /// <summary>Серое поле вокруг бледного значка вещества в сводке: значок виден, а цвет остаётся своим.</summary>
        static readonly Color PaleFrame = Color.FromArgb(0x8A, 0x92, 0x9C);

        readonly List<Run> runs = new List<Run>();

        void Add(string text, Style style = Style.Normal, Color? color = null)
        {
            this.runs.Add(new Run { Text = text, Style = style, Color = color });
        }

        void Line(string text = "", Style style = Style.Normal)
        {
            this.Add(text + "\n", style);
        }

        /// <summary>
        /// (`AMBER322`) Раскладка файла в поля редактора: с ней сводка начинается сверкой «в файле → в полях»,
        /// упрощениями и списком «не используется в геометрии» (свёрнут, пока <see cref="UnusedExpanded"/> ложь),
        /// а прежние разделы идут ниже под заголовком «Подробности файла». Ставится до первого чтения сводки.
        /// </summary>
        public GdmlFieldMap Layout;

        /// <summary>Части, взятые из файла в поля; None — раскладка не применена (отказ: файл только смотрится).</summary>
        public GdmlParts LayoutParts;

        public bool UnusedExpanded;

        /// <summary>Строки поверх сверки: «сопоставление изменено вручную», «детали нет в копии».</summary>
        public readonly List<string> LayoutNotes = new List<string>();

        public static string PartName(GdmlParts part)
        {
            return part == GdmlParts.Detector ? GdmlText.T("GdmlPartDetector", "detector")
                 : part == GdmlParts.Sample ? GdmlText.T("GdmlPartSample", "sample")
                 : GdmlText.T("GdmlPartShield", "shield");
        }

        /// <summary>Итог одной фразой: что из файла взято в поля. Та же фраза — в строке над вкладками редактора.</summary>
        public static string TakenText(GdmlFieldMap layout, GdmlParts parts)
        {
            if (layout == null || (layout.Found & GdmlParts.Detector) == 0 || (parts & GdmlParts.Detector) == 0)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlRefused", "Fields unchanged: {0}."),
                    layout == null || layout.Refusal == null ? GdmlText.T("GdmlWhyNoCrystal", "the crystal was not recognised") : layout.Refusal);
            }

            List<string> taken = new List<string>(), left = new List<string>();
            foreach (GdmlParts part in new[] { GdmlParts.Detector, GdmlParts.Sample, GdmlParts.Shield })
            {
                ((parts & layout.Found & part) != 0 ? taken : left).Add(PartName(part));
            }

            string text = string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlTaken", "Taken from the file: {0}."), string.Join(", ", taken.ToArray()));
            return left.Count == 0 ? text : text + " " + string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlLeftToHand", "Set by hand: {0}."), string.Join(", ", left.ToArray()));
        }

        /// <summary>Сумма «не используется в геометрии»: число деталей и масса, г.</summary>
        public static string UnusedText(GdmlFieldMap layout, GdmlParts parts)
        {
            List<GdmlVolumeInstance> unused = layout.Unused(parts);
            double mass = 0.0;
            foreach (GdmlVolumeInstance volume in unused)
            {
                mass += double.IsNaN(volume.MassG) ? 0.0 : volume.MassG;
            }

            return string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlUnusedHeader", "Not used in the geometry: parts {0}, {1} g"),
                unused.Count, GdmlSketch.Fmt(mass));
        }

        bool BuildLayout()
        {
            GdmlFieldMap layout = this.Layout;
            if (layout == null)
            {
                return false;
            }

            this.Line();
            this.Line(TakenText(layout, this.LayoutParts), Style.Strong);
            foreach (string note in this.LayoutNotes)
            {
                this.Line(note, Style.Dim);
            }

            GdmlParts parts = this.LayoutParts & layout.Found;
            if ((parts & GdmlParts.Detector) == 0)
            {
                return false;
            }

            this.Line();
            this.Line(GdmlText.T("GdmlMapHeader", "In the file → in the fields"), Style.Head);
            foreach (GdmlParts part in new[] { GdmlParts.Detector, GdmlParts.Sample, GdmlParts.Shield })
            {
                bool head = false;
                foreach (GdmlMapLine line in layout.Lines)
                {
                    if (line.Part != part || ((parts & part) == 0 && !(part == GdmlParts.Sample && layout.SampleProblem != null)))
                    {
                        continue;
                    }

                    if (!head)
                    {
                        string name = PartName(part);
                        this.Line(char.ToUpper(name[0], CultureInfo.CurrentCulture) + name.Substring(1), Style.Strong);
                        head = true;
                    }

                    this.Line("    " + line.InFile);
                    this.Line("    → " + line.InFields, Style.Dim);
                }
            }

            bool simplified = false;
            foreach (KeyValuePair<GdmlParts, string> item in layout.Simplifications)
            {
                if ((parts & item.Key) == 0)
                {
                    continue;
                }

                if (!simplified)
                {
                    this.Line();
                    this.Line(GdmlText.T("GdmlSimplHeader", "Simplifications"), Style.Head);
                    simplified = true;
                }

                this.Line("    " + item.Value);
            }

            foreach (string mesh in layout.OpenMeshes)
            {
                this.Line("    " + string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSimplOpenMesh", "Open mesh {0} is not measured by the rays."), mesh), Style.Dim);
            }

            List<GdmlVolumeInstance> unused = layout.Unused(parts);
            if (unused.Count > 0)
            {
                this.Line();
                this.Line((this.UnusedExpanded ? "▾ " : "▸ ") + UnusedText(layout, parts), Style.Head);
                if (this.UnusedExpanded)
                {
                    foreach (GdmlVolumeInstance volume in unused)
                    {
                        this.Line("    " + string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapItem", "{0} — {1}, {2} g"),
                            volume.VolumeName, volume.MaterialName.StartsWith("G4_", StringComparison.Ordinal) ? volume.MaterialName.Substring(3) : volume.MaterialName,
                            double.IsNaN(volume.MassG) ? "?" : GdmlSketch.Fmt(volume.MassG)));
                    }
                }
            }

            return true;
        }

        void Build()
        {
            if (this.runs.Count > 0)
            {
                return;
            }

            GdmlGeometry g = this.geometry;
            string unitCm3 = GdmlText.T("GdmlUnitCm3", "cm3");
            string unitG = GdmlText.T("GdmlUnitG", "g");
            this.Line(Path.GetFileName(g.FileName ?? ""), Style.Title);

            // (`AMBER301`, fixGdml2) Габарит — тот же, что у рамки чертежа: без оболочки вокруг всей сцены и без тел за рамкой.
            // Прежде здесь стоял габарит мира `lht.gdml`, 3000 × 500 × 3000 мм, а прибор в нём — 457 × 300 × 570.
            string overall = this.SceneMin != null
                ? SizeText(this.SceneMin, this.SceneMax, g.Axis, false)
                : "?";
            this.Line(string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlOverviewLine", "{0} volume(s), {1} material(s), overall size {2}."),
                // (`AMBER301`) Счёт веществ — тот же, что в журнале окна (`GdmlGeometry.MaterialRowCount`): вещество только
                // непрочитанного содержимого (`<paramvol>`, `<replicavol>` вдоль rho/phi) считается, строка «(нет вещества)» — нет.
                g.Volumes.Count, g.MaterialRowCount, overall));
            if (this.envelopeOrder.Count > 0)
            {
                List<string> names = new List<string>();
                foreach (GdmlVolumeInstance envelope in this.envelopeOrder)
                {
                    names.Add(envelope.VolumeName + " " + SizeText(envelope, g.Axis));
                }

                this.Line(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlOverviewEnvelope", "Without the envelope around the whole scene, not shown on the drawing: {0}."),
                    string.Join(", ", names.ToArray())), Style.Dim);
            }

            // Числа — те же, что у подписей чертежа «Не нарисовано»: одна мерка читателя, деталями.
            if (g.UnknownSolids > 0 || g.UnplacedVolumes > 0 || g.UnknownMaterials > 0)
            {
                this.Line(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlOverviewProblems", "Unsupported bodies: {0}, placement or extent not read: {1}, unknown compositions: {2}."),
                    g.UnknownSolids, g.UnplacedVolumes, g.UnknownMaterials), Style.Strong);
            }

            // Главное: то, что подписано на чертеже.
            HashSet<GdmlVolumeInstance> named = new HashSet<GdmlVolumeInstance>();
            bool any = this.BuildLayout();
            foreach (GdmlVolumeInstance volume in new[] { this.Crystal, this.Sample, this.Vessel })
            {
                if (volume != null && this.Layout == null)
                {
                    if (!any)
                    {
                        this.Line();
                        this.Line(GdmlText.T("GdmlOverviewMain", "Main parts"), Style.Head);
                        any = true;
                    }

                    named.Add(volume);
                    this.VolumeRow(volume, RoleName(this.RoleOf(volume)));
                }
            }

            if (this.DeviceMin != null && this.DeviceBody != null && this.Layout == null)
            {
                if (!any)
                {
                    this.Line();
                    this.Line(GdmlText.T("GdmlOverviewMain", "Main parts"), Style.Head);
                }

                this.Add(RoleName(GdmlRole.Device) + ": ", Style.Strong);
                this.Line(this.DeviceSizeText());
            }

            if (this.Layout != null)
            {
                this.Line();
                this.Line(GdmlText.T("GdmlDetails", "File details"), Style.Title);
            }

            this.Line();
            this.Line(GdmlText.T("GdmlOverviewMaterials", "Materials, heaviest first"), Style.Head);
            foreach (GdmlMaterialRow row in this.Materials)
            {
                // Газ почти белый: на белой сводке значок потерялся бы. (Четвёртый заход, по критику `critGdml2`) Цвет значка —
                // всегда цвет вещества, а бледный обводится серым полем знака: прежняя подмена на C4CCD6 красила пять из шести
                // оттенков газа одним цветом, и решение Amber «Оттенки газа (Рекомендую)» в сводке не выполнялось.
                bool pale = row.Color.GetBrightness() > 0.85f;
                this.runs.Add(new Run { Text = "■", Style = Style.Normal, Color = row.Color, Back = pale ? PaleFrame : (Color?)null });
                this.Add(" ");
                this.Add(row.Display, Style.Strong);
                this.Add(row.Role == GdmlRole.None ? "" : " (" + RoleName(row.Role).ToLowerInvariant() + ")");
                // У вещества только непрочитанных копий деталей нет: «0 дет.» не пишется.
                this.Line(": " + row.MassText() + row.UncountedText()
                          + (row.Volumes > 0 ? ", " + row.Volumes.ToString(CultureInfo.InvariantCulture) + " " + GdmlText.T("GdmlOverviewVolumesShort", "vol.") : ""));
                string composition = this.CompositionText(row);
                if (composition.Length > 0)
                {
                    this.Line("    " + composition, Style.Dim);
                }
            }

            List<GdmlVolumeInstance> unclear = new List<GdmlVolumeInstance>();
            List<GdmlVolumeInstance> large = new List<GdmlVolumeInstance>();
            List<GdmlVolumeInstance> rest = new List<GdmlVolumeInstance>();
            foreach (GdmlVolumeInstance volume in g.Volumes)
            {
                if (volume == null || volume.Solid == null || volume.Solid.Kind == GdmlSolidKind.Unknown
                    || !volume.Solid.VolumeKnown || !volume.PlacementKnown)
                {
                    unclear.Add(volume);
                }
                else if (!named.Contains(volume))
                {
                    (volume.VolumeCm3 >= GdmlGeometry.LargeThreshold ? large : rest).Add(volume);
                }
            }

            large.Sort((a, b) => b.VolumeCm3.CompareTo(a.VolumeCm3));
            if (large.Count > 0)
            {
                this.Line();
                this.Line(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryLarge", "Large volumes (from {0} cm3):"), GdmlText.N(GdmlGeometry.LargeThreshold)), Style.Head);
                for (int i = 0; i < large.Count && i < NamedVolumes; i++)
                {
                    this.VolumeRow(large[i], null);
                }

                if (large.Count > NamedVolumes)
                {
                    rest.AddRange(large.GetRange(NamedVolumes, large.Count - NamedVolumes));
                }
            }

            if (rest.Count > 0)
            {
                this.Line();
                this.Line(GdmlText.T("GdmlOverviewRest", "The rest, in total"), Style.Head);
                this.RestRows(rest);
            }

            if (unclear.Count > 0)
            {
                this.Line();
                this.Line(GdmlText.T("GdmlSummaryUnclear", "Not measured:"), Style.Head);
                for (int i = 0; i < unclear.Count && i < NamedUnclear; i++)
                {
                    this.UnclearRow(unclear[i]);
                }

                if (unclear.Count > NamedUnclear)
                {
                    this.Line(string.Format(CultureInfo.InvariantCulture,
                        GdmlText.T("GdmlOverviewMore", "and {0} more"), unclear.Count - NamedUnclear), Style.Dim);
                }
            }

            foreach (GdmlMaterialRow row in this.Materials)
            {
                if (row.Material != null && row.Material.Unknown)
                {
                    this.Line(string.Format(CultureInfo.InvariantCulture,
                        GdmlText.T("GdmlSummaryUnknownMaterial", "  composition {0} is unknown: not in the file and not in the built-in G4 table"), row.Name).Trim());
                }
                else if (row.Material != null && row.Material.Partial)
                {
                    this.Line(string.Format(CultureInfo.InvariantCulture,
                        GdmlText.T("GdmlSummaryPartialMaterial", "  composition {0} is incomplete: unknown {1}"),
                        row.Name, string.Join(", ", row.Material.Missing.ToArray())).Trim());
                }
            }

            List<string> notes = this.NotesWithoutRepeats(unclear);
            if (notes.Count > 0)
            {
                this.Line();
                this.Line(GdmlText.T("GdmlSummaryWarnings", "Notes:"), Style.Head);
                foreach (string warning in notes)
                {
                    this.Line(warning);
                }
            }
        }

        /// <summary>
        /// Замечания без тех, что дословно повторяют причину в «Не измерено»: читатель пишет одну и ту же
        /// строку и в замечания, и телу. Для них остаётся строка у самого тела с его именем и размером.
        /// </summary>
        List<string> NotesWithoutRepeats(List<GdmlVolumeInstance> unclear)
        {
            List<string> reasons = new List<string>();
            foreach (GdmlVolumeInstance volume in unclear)
            {
                if (volume != null && volume.Solid != null && !volume.Solid.VolumeKnown && volume.Solid.Note.Length > 0)
                {
                    reasons.Add(volume.Solid.Note);
                }
            }

            List<string> kept = new List<string>();
            foreach (string warning in this.geometry.Warnings)
            {
                bool repeat = false;
                foreach (string reason in reasons)
                {
                    repeat = repeat || warning.EndsWith(reason, StringComparison.Ordinal);
                }

                if (!repeat)
                {
                    kept.Add(warning);
                }
            }

            return kept;
        }


        string CompositionText(GdmlMaterialRow row)
        {
            GdmlMaterial material = row.Material;
            if (material == null || material.Name.Length == 0)
            {
                return GdmlText.T("GdmlSummaryNoMaterial", "(no material)");
            }

            if (material.Unknown)
            {
                return GdmlText.T("GdmlSummaryCompositionUnknown", "(composition unknown)");
            }

            string density = material.HasDensity
                ? GdmlText.N(material.Density) + " " + GdmlText.T("GdmlOverviewDensity", "g/cm3")
                : GdmlText.T("GdmlOverviewNoDensity", "density unknown");

            // Элемент и доля через двоеточие: перенос «F | 0.1000» читался как два числа. Замер RichEdit на шести
            // ширинах: пробел, U+00A0 и «=» рвутся 9 раз из 54, двоеточие ни разу; U+202F не рвётся, но в шрифте окна нулевой ширины.
            string fractions = material.CompositionText().Replace(" ", ":").Replace(",:", ", ");
            return density + "; " + GdmlText.T("GdmlOverviewFractions", "mass fractions") + " " + fractions;
        }

        void VolumeRow(GdmlVolumeInstance volume, string role)
        {
            string unitCm3 = GdmlText.T("GdmlUnitCm3", "cm3");
            string unitG = GdmlText.T("GdmlUnitG", "g");
            if (role != null)
            {
                this.Add(role + ": ", Style.Strong);
            }

            this.Line(volume.VolumeName, role == null ? Style.Strong : Style.Normal);
            string volumeText = volume.VolumeCm3 >= 0.0 ? GdmlSketch.Fmt(volume.VolumeCm3) + " " + unitCm3 : "? " + unitCm3;
            string massText = !double.IsNaN(volume.MassG) ? GdmlSketch.Fmt(volume.MassG) + " " + unitG : "? " + unitG;
            GdmlMaterialRow row;
            string material = this.rows.TryGetValue(volume.MaterialName ?? "", out row) ? row.Display : "?";
            this.Line("    " + SizeText(volume, this.geometry.Axis) + ", " + volumeText + ", " + massText + ", " + material, Style.Dim);
            if (volume.MassNote.Length > 0)
            {
                this.Line("    " + volume.MassNote, Style.Dim);
            }
        }

        void RestRows(List<GdmlVolumeInstance> rest)
        {
            double volumeSum = 0.0, massSum = 0.0;
            int massUnknown = 0;
            SortedDictionary<string, double[]> byMaterial = new SortedDictionary<string, double[]>(StringComparer.Ordinal);
            foreach (GdmlVolumeInstance item in rest)
            {
                // Своё, без прямых детей: иначе вложенные тома сложились бы дважды.
                volumeSum += item.OwnVolumeCm3 >= 0.0 ? item.OwnVolumeCm3 : Math.Max(0.0, item.VolumeCm3);
                string name = string.IsNullOrEmpty(item.MaterialName) ? "?" : item.MaterialName;
                double[] cell;
                if (!byMaterial.TryGetValue(name, out cell))
                {
                    cell = new double[3];
                    byMaterial[name] = cell;
                }

                cell[0]++;
                if (double.IsNaN(item.MassG))
                {
                    massUnknown++;
                    cell[2]++;
                }
                else
                {
                    massSum += item.MassG;
                    cell[1] += item.MassG;
                }
            }

            this.Line(string.Format(CultureInfo.InvariantCulture,
                GdmlText.T("GdmlOverviewRestLine", "{0} volume(s), {1} cm3, {2} g"),
                rest.Count, GdmlSketch.Fmt(volumeSum), GdmlSketch.Fmt(massSum)));
            foreach (KeyValuePair<string, double[]> cell in byMaterial)
            {
                GdmlMaterialRow row;
                string display = this.rows.TryGetValue(cell.Key, out row) ? row.Display : cell.Key;
                this.Line(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummarySmallByMaterial", "  {0}: {1} volume(s), {2} g"),
                    display, (int)cell.Value[0], cell.Value[2] > 0 && cell.Value[1] <= 0.0 ? "?" : GdmlSketch.Fmt(cell.Value[1])), Style.Dim);
            }

            if (massUnknown > 0)
            {
                this.Line(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummarySmallUnknownMass", "  mass unknown: {0} volume(s)"), massUnknown), Style.Dim);
            }
        }

        void UnclearRow(GdmlVolumeInstance volume)
        {
            if (volume == null)
            {
                return;
            }

            if (volume.Solid == null || volume.Solid.Kind == GdmlSolidKind.Unknown)
            {
                this.Line(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlSummaryUnknownSolid", "  unsupported body: {0} - {1}"),
                    volume.VolumeName, volume.Solid == null ? "?" : volume.Solid.Tag).Trim());
                return;
            }

            this.Line(volume.VolumeName, Style.Strong);
            StringBuilder reason = new StringBuilder(SizeText(volume, this.geometry.Axis));
            if (!volume.PlacementKnown)
            {
                reason.Append("; ").Append(GdmlText.T("GdmlSummaryNoPlacement", "placement is not read"));
            }

            if (volume.MassNote.Length > 0)
            {
                reason.Append("; ").Append(volume.MassNote);
            }

            if (!volume.Solid.VolumeKnown && volume.Solid.Note.Length > 0)
            {
                reason.Append("; ").Append(volume.Solid.Note);
            }

            this.Line("    " + reason, Style.Dim);
        }

        /// <summary>Та же сводка простым текстом: читает проба и буфер обмена.</summary>
        public string SummaryText()
        {
            this.Build();
            StringBuilder text = new StringBuilder();
            foreach (Run run in this.runs)
            {
                text.Append(run.Text);
            }

            return text.ToString();
        }

        /// <summary>
        /// Сводка в RTF: заголовок, подзаголовки и цветные значки веществ. Текст собирается сразу: через
        /// выделение в окне было бы дольше, а пятьсот строк замечаний — не предел.
        /// </summary>
        public string SummaryRtf(string fontName, float fontSize)
        {
            this.Build();
            List<Color> colors = new List<Color> { Color.FromArgb(0x20, 0x20, 0x20), Color.FromArgb(0x5A, 0x5A, 0x5A) };
            foreach (Run run in this.runs)
            {
                if (run.Color != null && !colors.Contains(run.Color.Value))
                {
                    colors.Add(run.Color.Value);
                }

                if (run.Back != null && !colors.Contains(run.Back.Value))
                {
                    colors.Add(run.Back.Value);
                }
            }

            StringBuilder rtf = new StringBuilder();
            rtf.Append("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil ").Append(fontName).Append(";}}{\\colortbl;");
            foreach (Color color in colors)
            {
                rtf.AppendFormat(CultureInfo.InvariantCulture, "\\red{0}\\green{1}\\blue{2};", color.R, color.G, color.B);
            }

            rtf.Append("}\\f0 ");
            int normal = (int)Math.Round(fontSize * 2.0f);
            bool lineStart = true;
            foreach (Run run in this.runs)
            {
                // Отступ — свойством абзаца: четыре пробела держат только первую строку, а перенесённая уходит влево.
                string body = run.Text;
                if (lineStart)
                {
                    bool indented = body.StartsWith("    ", StringComparison.Ordinal);
                    rtf.Append(indented ? "\\pard\\sa20\\li240 " : "\\pard\\sa20 ");
                    body = indented ? body.TrimStart(' ') : body;
                }

                lineStart = body.EndsWith("\n", StringComparison.Ordinal);
                int size = run.Style == Style.Title ? normal + 4 : normal;
                bool bold = run.Style == Style.Title || run.Style == Style.Head || run.Style == Style.Strong;
                int colorIndex = run.Color != null ? colors.IndexOf(run.Color.Value) + 1 : run.Style == Style.Dim ? 2 : 1;
                int backIndex = run.Back != null ? colors.IndexOf(run.Back.Value) + 1 : 0;
                rtf.AppendFormat(CultureInfo.InvariantCulture, "\\fs{0}\\cf{1}{2}\\highlight{3} ", size, colorIndex, bold ? "\\b" : "\\b0", backIndex);
                foreach (char c in body)
                {
                    if (c == '\n')
                    {
                        rtf.Append("\\par ");
                    }
                    else if (c == '\\' || c == '{' || c == '}')
                    {
                        rtf.Append('\\').Append(c);
                    }
                    else if (c < 128)
                    {
                        rtf.Append(c);
                    }
                    else
                    {
                        rtf.Append("\\u").Append(((short)c).ToString(CultureInfo.InvariantCulture)).Append('?');
                    }
                }
            }

            rtf.Append('}');
            return rtf.ToString();
        }
    }
}
