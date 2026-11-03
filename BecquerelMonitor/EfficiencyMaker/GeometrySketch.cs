using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        /// <summary>
        /// Зазор между отражателем и корпусом (`AMBER1`). Цвет светлее корпуса
        /// и холоднее отражателя: слой по умолчанию ВОЗДУХ, и он не должен
        /// читаться как вещество обвязки.
        /// </summary>
        static readonly Color GapColor = Color.FromArgb(0xE8, 0xEE, 0xF6);
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

        /// <summary>
        /// (`AMBER282`) Рисовать ли защиту в расчёте. На вкладке «Источник» редактора — нет:
        /// защита переехала на свою вкладку (Amber 03.11.2026: «Сделай новую вкладку рядом с
        /// текущими: Детектор, Источник. Обзови ее Защита. И перенеси все это туда»).
        /// </summary>
        public bool ShowShield { get; set; } = true;

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

        int crowded;
        int crowdedReported = -1;

        /// <summary>
        /// Сколько размерных чисел последней отрисовки встали ПРИЖАТЫМИ — то
        /// есть ни одно из 40+ мест по ГОСТ не оказалось свободным, и число
        /// напечатано в наименее занятом, вплотную к соседу или к краю поля.
        /// </summary>
        /// <remarks>
        /// ⛔ `E40`. Прижимать число правильно — потерянное число хуже
        /// прижатого, и <see cref="Place"/> так и делает. Дефектом был не сам
        /// прижим, а его НЕМОТА: узнать о нём можно было только прогнав
        /// `EditorShot --check`, а тот меряет 14 заготовленных сцен. У
        /// геометрии, введённой человеком в поля редактора, читателя не было
        /// вовсе — чертёж молча выдавал нечитаемое место.
        ///
        /// Теперь читателя два: это свойство (его берёт проба) и запись в
        /// журнал приложения <c>becqmoni.log</c> через <see cref="Trace"/> —
        /// то есть при жалобе человека подробности есть откуда взять.
        ///
        /// ⚠ Ноль — это «все числа встали свободно», а не «не считали»:
        /// счётчик сбрасывается в начале КАЖДОЙ отрисовки, вместе с
        /// <see cref="Labels"/>.
        /// </remarks>
        public int CrowdedLabels
        {
            get { return this.crowded; }
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
            // (`AMBER282`) Условный разрыв воздуха полости защиты: отрезок
            // [breakZ0, breakZ1] сжат в breakPx точек, всё ниже него сдвинуто вверх.
            double u;
            if (this.breakPx > 0f && z > this.breakZ0)
            {
                double u0 = (this.breakZ0 - this.worldTop) * this.scale;
                u = z < this.breakZ1
                    ? u0 + (z - this.breakZ0) / (this.breakZ1 - this.breakZ0) * this.breakPx
                    : u0 + this.breakPx + (z - this.breakZ1) * this.scale;
            }
            else
            {
                u = (z - this.worldTop) * this.scale;
            }

            // (`AMBER282`, Amber 03.11.2026: «У тебя эскиз верх ногами 🙂 Как ты думаешь,
            // выпадет ли детектор из домика?») Сцена в защите рисуется дном ВНИЗ: проба
            // стоит на дне полости, колодец открыт вверх, прибор входит сверху. Ось z
            // переноса та же (дно — со стороны лица кристалла, −z), чертёж отражён.
            return this.flipZ ? (float)(this.padTop + this.drawnPx - u) : (float)(this.padTop + u);
        }

        /// <summary>(`AMBER282`) Чертёж отражён по вертикали (сцена в защите, дно внизу) и высота мира на чертеже, точек.</summary>
        bool flipZ;

        double drawnPx;

        /// <summary>(`AMBER282`) Разрыв воздуха полости защиты (мир, мм) и его высота на чертеже, точек; 0 — разрыва нет.</summary>
        double breakZ0, breakZ1;

        float breakPx;

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
            // (`AMBER282`) Над полосами защиты, которые стоят в точках над миром: у
            // отражённого чертежа сверху — крышка и мировой низ.
            float bands = this.shieldPx != null ? (this.flipZ ? this.shieldPx.LidTotal : this.shieldPx.FloorTotal) : 0f;
            if (this.flipZ)
            {
                double zBottom = this.worldTop + (this.drawnPx - this.breakPx) / this.scale + (this.breakZ1 - this.breakZ0);
                return zBottom + (pixels + bands) / this.scale;
            }

            return this.worldTop - (pixels + bands) / this.scale;
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
            this.obstacles.Clear();
            // ⛔ `E40`. Счётчик прижатых чисел живёт РОВНО ОДНУ отрисовку, как
            // и список подписей: иначе он копил бы прижимы прошлых кадров и
            // показывал бы беду там, где её уже нет.
            this.crowded = 0;

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
            // (`AMBER1`) Зазор — такой же слой обвязки и переставляется вместе
            // с нею.
            double tfg = Math.Max(m.FrontGapThickness, 0.0);
            double tsg = Math.Max(m.SideGapThickness, 0.0);
            // Та же перестановка, что в симуляторе: к пробе обращена обвязка
            // ТОЙ стороны, у которой она стоит.
            if (m.Facing == GeometryDetectorFacing.Side)
            {
                double t = tfr; tfr = tsr; tsr = t;
                t = tfc; tfc = tsc; tsc = t;
                t = tfg; tfg = tsg; tsg = t;
            }
            double tm = Math.Max(m.MountingThickness, 0.0);
            if (!(halfWidth > 0.0) || !(height > 0.0))
            {
                return;
            }

            double outerHalf = halfWidth + tsr + tsg + tsc;
            double zFace = -(tfr + tfg + tfc);
            double zBack = height + tm;

            double left = -outerHalf, right = outerHalf, top = zFace, bottom = zBack;
            // (`AMBER282`, дизайн 03.11.2026) Защита в кадр входит ПОЛОСТЬЮ в мировом
            // масштабе, а слои — полосами в ТОЧКАХ (`ShieldPx`): честная сталь 38.55 мм
            // вокруг пробы Ø80 сжимала пробу и прибор в пятно.
            ShieldBox shield = new ShieldBox();
            bool hasShield = this.Mode != SketchMode.Detector && this.ShowShield && ShieldInSketch(m, out shield);
            double envBottom = bottom;
            if (this.Mode != SketchMode.Detector)
            {
                double sl, sr, st, sb;
                SourceBounds(m, zFace, out sl, out sr, out st, out sb);
                left = Math.Min(left, sl);
                right = Math.Max(right, sr);
                top = Math.Min(top, st);
                bottom = Math.Max(bottom, sb);
                envBottom = bottom;

                if (hasShield)
                {
                    left = Math.Min(left, -shield.Cavity);
                    right = Math.Max(right, shield.Cavity);
                    top = Math.Min(top, shield.FloorFace);
                    bottom = Math.Max(bottom, shield.LidFace);
                }
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

            // (`AMBER282`) Под защитой — размер диаметра полости: ему нужна своя полоса;
            // по бокам толщин больше нет (они в легенде), и поле уже.
            if (this.Mode == SketchMode.Source && hasShield)
            {
                MarginBottom = 46;
                MarginX = 72;
            }

            // (`AMBER225`) Табличка объёма стоит в левом верхнем углу, а над
            // телом там же идёт верхний размер с числом. Чертёж опускается
            // ровно настолько, чтобы число размера встало НИЖЕ таблички:
            // иначе размерная линия прошла бы сквозь надпись. 23 точки — это
            // `AboveTop(22)` верхнего размера плюс точка, на которую
            // `SpotsH` поднимает над линией его число.
            string volumeText = this.VolumeText(m);
            SizeF volumeSize = SizeF.Empty;
            if (volumeText != null)
            {
                volumeSize = g.MeasureString(volumeText, this.Font);
                MarginTop = Math.Max(MarginTop,
                    (int)Math.Ceiling(VolumePlateBottom(volumeSize) + Gap + 23f + volumeSize.Height));
            }
            double worldWidth = Math.Max(right - left, 1e-6);
            double worldHeight = Math.Max(bottom - top, 1e-6);

            // (`AMBER282`, Amber 03.11.2026, дословно: «Не нужен колодец "в плане" нужны: вид
            // сбоку, вид сверху. Причём вид сверху должен полностью описывать геометрию
            // сверху, не только колодец с детектором, но и саму защиту.») У пробы с защитой
            // или колодцем-трапецией — ДВА вида одним масштабом: разрез («вид сбоку») и план
            // («вид сверху»). Друг под другом или рядом — как выходит крупнее. Легенда
            // защиты — полосой снизу во всю ширину.
            bool plan = this.Mode == SketchMode.Source && m.Scene == GeometrySceneKind.None
                        && (hasShield || m.HasTrapezoidHole);
            double planR = 0.0;
            if (plan)
            {
                double envR, envTop, envBot;
                GeometryScenes.SceneEnvelopeMm(m, out envR, out envTop, out envBot);
                planR = Math.Max(envR, hasShield ? shield.Cavity : 0.0);
                plan = planR > 0.0;
            }

            this.LayoutSideBlock(g, m, hasShield);
            float reserveY = this.legendRect.IsEmpty ? 0f : this.legendRect.Height + 8f;
            const int PlanMarginTop = 34;
            int planMarginBottom = hasShield ? 50 : 12;

            // Масштаб — по ПОЛОСТИ и по рисуемым полосам слоёв: полосы в точках зависят
            // от масштаба (тонкий слой — не меньше 3 точек, толстый — не больше предела),
            // поэтому три прохода; каждый следующий масштаб не меньше предыдущего.
            this.shieldPx = null;
            this.breakZ0 = this.breakZ1 = 0.0;
            this.breakPx = 0f;
            this.scale = 0.0;
            this.planStacked = true;
            for (int pass = 0; pass < 3; pass++)
            {
                ShieldPx px = hasShield ? this.BandsPx(m, this.scale) : null;
                float side = px != null ? px.SideTotal : 0f;
                float vert = px != null ? px.FloorTotal + px.LidTotal : 0f;
                double breakLen = this.breakZ1 - this.breakZ0;
                double sideH = Math.Max(worldHeight - breakLen, 1e-6);
                double sx = (this.Width - 2.0 * MarginX - 2.0 * side) / worldWidth;
                double sy = (this.Height - MarginTop - MarginBottom - reserveY - vert - this.breakPx) / sideH;
                bool vertical = sy < sx;
                if (!plan)
                {
                    this.scale = Math.Min(sx, sy);
                }
                else
                {
                    double dp = 2.0 * planR;
                    // Друг под другом.
                    double sx1 = (this.Width - 2.0 * MarginX - 2.0 * side) / Math.Max(worldWidth, dp);
                    double sy1 = (this.Height - MarginTop - MarginBottom - PlanMarginTop - planMarginBottom
                                  - reserveY - vert - 2.0 * side - this.breakPx) / (sideH + dp);
                    // Рядом.
                    double sx2 = (this.Width - 3.0 * MarginX - 4.0 * side) / (worldWidth + dp);
                    double sy2 = Math.Min(sy, (this.Height - PlanMarginTop - planMarginBottom - reserveY - 2.0 * side) / dp);
                    double s1 = Math.Min(sx1, sy1), s2 = Math.Min(sx2, sy2);
                    this.planStacked = s1 >= s2;
                    this.scale = Math.Max(s1, s2);
                    vertical = this.planStacked ? sy1 < sx1 : sy2 < sx2;
                }

                this.shieldPx = px;

                // Второй ярус (дизайн, п. 3): кадр упёрся в высоту, а воздух полости между
                // сценой и крышкой больше 40 % её высоты — воздух сжимается до 24 точек
                // условным разрывом. Решается один раз, на первом проходе.
                if (pass == 0 && hasShield && this.Mode == SketchMode.Source && vertical)
                {
                    double air = shield.LidFace - envBottom;
                    if (air > 0.4 * (shield.LidFace - shield.FloorFace))
                    {
                        this.breakZ0 = envBottom + 0.1 * air;
                        this.breakZ1 = shield.LidFace - 0.1 * air;
                        this.breakPx = 24f;
                    }
                }
            }

            if (hasShield)
            {
                this.shieldPx = this.BandsPx(m, this.scale);
            }

            if (!(this.scale > 0.0) || double.IsInfinity(this.scale))
            {
                return;
            }

            this.worldLeft = left;
            this.worldTop = top;
            // (`AMBER282`) Сцена в защите — дном вниз (`flipZ`): сверху полосы крышки.
            // Так же и на вкладке «Источник», где защита не рисуется: та же проба в той же
            // установке не должна на соседней вкладке висеть вверх ногами.
            // И в миниатюре окна прибора (Amber 03.11.2026: «А ты не забыл перевернуть вот
            // это?») — один вид, но той же ориентации, что вид сбоку редактора.
            this.flipZ = (this.Mode == SketchMode.Source || this.Mode == SketchMode.Overview) && m.ShieldInScene;
            float floorPx = this.shieldPx != null ? (this.flipZ ? this.shieldPx.LidTotal : this.shieldPx.FloorTotal) : 0f;
            float lidPx = this.shieldPx != null ? (this.flipZ ? this.shieldPx.FloorTotal : this.shieldPx.LidTotal) : 0f;
            float sidePx = this.shieldPx != null ? this.shieldPx.SideTotal : 0f;
            double drawnHeight = (worldHeight - (this.breakZ1 - this.breakZ0)) * this.scale + this.breakPx;
            this.drawnPx = drawnHeight;
            this.planView = plan;
            this.planRadius = planR;
            if (!plan)
            {
                this.padLeft = (int)((this.Width - worldWidth * this.scale) / 2.0);
                this.padTop = MarginTop + (int)floorPx + (int)((this.Height - MarginTop - MarginBottom - reserveY
                                                               - floorPx - lidPx - drawnHeight) / 2.0);
            }
            else if (this.planStacked)
            {
                double planH = PlanMarginTop + 2.0 * planR * this.scale + 2.0 * sidePx + planMarginBottom;
                double used = MarginTop + floorPx + drawnHeight + lidPx + MarginBottom + planH;
                int slack = (int)Math.Max(0.0, (this.Height - reserveY - used) / 2.0);
                this.padLeft = (int)((this.Width - worldWidth * this.scale) / 2.0);
                this.padTop = MarginTop + (int)floorPx + slack;
                float sideBottom = (float)(this.padTop + drawnHeight + lidPx + MarginBottom);
                this.planCenter = new PointF(0.5f * this.Width,
                                             (float)(sideBottom + PlanMarginTop + sidePx + planR * this.scale));
                this.planTitleAt = new PointF(8f, sideBottom + 4f);
            }
            else
            {
                double content = (worldWidth + 2.0 * planR) * this.scale + 4.0 * sidePx + 3.0 * MarginX;
                int slack = (int)Math.Max(0.0, (this.Width - content) / 2.0);
                this.padLeft = slack + MarginX + (int)sidePx;
                this.padTop = MarginTop + (int)floorPx + (int)((this.Height - MarginTop - MarginBottom - reserveY
                                                               - floorPx - lidPx - drawnHeight) / 2.0);
                float sideRight = (float)(this.padLeft + worldWidth * this.scale + sidePx);
                float cx = (float)(sideRight + MarginX + sidePx + planR * this.scale);
                float cy = (float)(this.padTop - floorPx + sidePx + planR * this.scale);
                cy = Math.Max(cy, (float)(PlanMarginTop + sidePx + planR * this.scale));
                this.planCenter = new PointF(cx, cy);
                this.planTitleAt = new PointF((float)(cx - planR * this.scale - sidePx), 6f);
            }

            if (this.Mode != SketchMode.Detector)
            {
                this.DrawSource(g, m, zFace);
            }

            this.DrawDetector(g, m, halfWidth, height, tfr, tsr, tfc, tsc, tm, tfg, tsg);

            // Табличка — ДО размеров: её прямоугольник уже лежит в `labels`,
            // и раскладка чисел (`Place`) обходит его, как любую подпись.
            if (volumeText != null)
            {
                this.DrawVolume(g, volumeText, volumeSize);
            }

            // (`AMBER282`) Подписи двух видов — тоже до размеров.
            this.DrawViewTitles(g, volumeText != null ? 12f + volumeSize.Width : 0f);

            if (this.Mode == SketchMode.Detector)
            {
                this.Annotate(g, m, halfWidth, height, boxDepthIntoPage,
                              tfr, tsr, tfc, tsc, tm, tfg, tsg);
            }
            else if (this.Mode == SketchMode.Source)
            {
                this.AnnotateSource(g, m, zFace);
                this.AnnotateShield(g, m);
                this.DrawShieldLegend(g, m);
                this.DrawPlanView(g, m);
            }
            else
            {
                this.AnnotateOverview(g, m);
            }

            this.ReportCrowded();
        }

        /// <summary>
        /// Сказать в журнал приложения, что размерным числам не хватило места.
        /// </summary>
        /// <remarks>
        /// ⛔ `E40`, вторая половина: сам счётчик — ещё не читатель.
        /// <see cref="CrowdedLabels"/> берёт проба, а человеку за экраном нужен
        /// след в <c>becqmoni.log</c> (<see cref="AppLog"/>): при жалобе «числа
        /// на чертеже слиплись» подробности иначе взять неоткуда, а
        /// `EditorShot --check` меряет 14 заготовленных сцен и о геометрии,
        /// введённой руками, не знает ничего.
        ///
        /// ⚠ Пишем ТОЛЬКО ПРИ ИЗМЕНЕНИИ числа, а не на каждый кадр.
        /// <see cref="OnPaint"/> зовётся на каждую перерисовку — при
        /// перетаскивании окна это десятки раз в секунду, и запись «в лоб»
        /// залила бы журнал (порог подрезки 2 МБ) за минуту, вытеснив из него
        /// всё остальное. Возврат к нулю сообщается тоже: человек должен видеть,
        /// что правкой полей он беду убрал.
        /// </remarks>
        void ReportCrowded()
        {
            if (this.crowded == this.crowdedReported)
            {
                return;
            }

            this.crowdedReported = this.crowded;
            Trace.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "GeometrySketch [{0}, {1}x{2}]: размерных чисел без свободного места — {3}",
                this.Mode, this.Width, this.Height, this.crowded));
        }

        /// <summary>
        /// Надпись об объёме для левого верхнего угла (`AMBER225`, Amber
        /// 15.10.2026: «При редактировании геометрии детектора и образца
        /// указывать объём в см^3 на скетче … при редактировании детектора -
        /// объём детектора; при редактировании образца - объём образца. если
        /// объём определить нельзя (лунка, поле, ISO) ничего не указывать,
        /// оставить пусто»; вид — словами, её же выбором вопросником).
        /// </summary>
        /// <remarks>
        /// <c>null</c> — надписи нет. У пробы её нет на всех трёх полевых
        /// сценах (<see cref="GeometrySceneKind.Ground"/>,
        /// <see cref="GeometrySceneKind.Borehole"/>,
        /// <see cref="GeometrySceneKind.Iso"/>): у грунта объём — число из
        /// правила свободного пробега, а не размер пробы. Нет её и у точечного
        /// источника — объёма у точки нет (<see cref="GeometryModel.HasSampleVolume"/>).
        /// Объём пробы — тем же счётом, что строка сцены в редакторе и сам
        /// расчёт (<see cref="GeometryScenes.SampleVolumeCm3"/>): у маринелли
        /// это разность двух тел, и вторая формула разъехалась бы с первой
        /// молча. Миниатюра (<see cref="SketchMode.Overview"/>) — не
        /// редактирование, и надписи там нет.
        /// </remarks>
        string VolumeText(GeometryModel m)
        {
            double volume;
            string format;
            if (this.Mode == SketchMode.Detector)
            {
                volume = GeometryScenes.CrystalVolumeCm3(m);
                format = Resources.EfficiencySketchCrystalVolume;
            }
            else if (this.Mode == SketchMode.Source
                     && m.Scene == GeometrySceneKind.None && m.HasSampleVolume)
            {
                volume = GeometryScenes.SampleVolumeCm3(m);
                format = Resources.EfficiencySketchSampleVolume;
            }
            else
            {
                return null;
            }

            if (!(volume > 0.0) || double.IsInfinity(volume))
            {
                return null;
            }

            // Четыре значащих, как у размеров; от тысячи — целым числом:
            // `G4` печатал бы 12 л кюветы как «1.2E+04».
            string number = volume < 1000.0
                ? volume.ToString("G4", CultureInfo.InvariantCulture)
                : volume.ToString("F0", CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture, format, number);
        }

        /// <summary>Низ таблички объёма, точек от верха поля.</summary>
        static float VolumePlateBottom(SizeF size)
        {
            return 4f + size.Height + 6f;
        }

        /// <summary>
        /// Табличка объёма — той же подложкой и в том же углу, что табличка
        /// миниатюры, и через <see cref="Print"/>: подпись мимо него для
        /// приёмки (`EditorShot --check`) не существует.
        /// </summary>
        void DrawVolume(Graphics g, string text, SizeF size)
        {
            using (Brush plate = new SolidBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF)))
            using (Brush ink = new SolidBrush(Ink))
            {
                g.FillRectangle(plate, 4f, 4f, size.Width + 8f, VolumePlateBottom(size) - 4f);
                this.Print(g, ink, text, null, 8f, 6f, size);
            }
        }

        /// <summary>
        /// (`AMBER241`(6), 20.10.2026) Строка габарита таблички: «подпись: ⌀80 × 80 мм».
        /// Размеры — четыре значащие цифры, как и были; между ними знак «×», единица — на
        /// языке интерфейса (`GeometryEditorUnitMm`, та же, что у полей редактора
        /// геометрии). Прежде «x» и «mm» были зашиты в формат, и русский интерфейс писал
        /// «⌀80 x 80 mm».
        /// </summary>
        static string Dims(string caption, string prefix, params double[] sizes)
        {
            string[] numbers = new string[sizes.Length];
            for (int i = 0; i < sizes.Length; i++)
            {
                numbers[i] = sizes[i].ToString("G4", CultureInfo.InvariantCulture);
            }
            return caption + ": " + prefix + string.Join(" × ", numbers) + " " + Resources.GeometryEditorUnitMm;
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
                ? Dims(Resources.EfficiencySketchDetector, "",
                       m.CrystalBoxX, m.CrystalBoxY, m.CrystalBoxZ)
                : Dims(Resources.EfficiencySketchDetector, "⌀",
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
                    // (`AMBER13` (б)) У поля форма точечная, но строка своя:
                    // расстояния у поля нет, есть радиус сферы.
                    lines.Add(m.Scene == GeometrySceneKind.Iso
                        ? Dims(Resources.EfficiencySketchIso, "", Math.Max(m.FieldRadius, 0.0))
                        : Dims(Resources.EfficiencySketchPoint, "", Math.Max(m.PointDistance, 0.0)));
                    break;

                case GeometrySourceType.Box:
                    lines.Add(Dims(Resources.EfficiencySketchBox, "",
                                   Math.Max(m.BoxSourceX, 0.0),
                                   Math.Max(m.BoxSourceY, 0.0),
                                   Math.Max(m.BoxSourceHeight, 0.0)));
                    lines.Add(Dims(Resources.EfficiencySketchDistance, "",
                                   Math.Max(m.BoxToDetectorDistance, 0.0)));
                    break;

                case GeometrySourceType.Cylinder:
                    lines.Add(Dims(Resources.EfficiencySketchCylinder, "⌀",
                                   Math.Max(m.BeakerDiameter, 0.0),
                                   Math.Max(m.SourceHeight, 0.0)));
                    lines.Add(Dims(Resources.EfficiencySketchDistance, "",
                                   Math.Max(m.BeakerToDetectorDistance, 0.0)));
                    break;

                default:
                    lines.Add(Dims(Resources.EfficiencySketchMarinelli, "⌀",
                                   Math.Max(m.MarinelliBeakerDiameter, 0.0),
                                   Math.Max(m.MarinelliBeakerHeight, 0.0)));
                    // (`AMBER282`) Колодец-трапеция — словами: на миниатюре разрез её не выдаёт.
                    if (m.HasTrapezoidHole)
                    {
                        lines.Add(Resources.EfficiencySketchTrapezoidHole + ": "
                                  + Format(m.MarinelliHoleBase1) + "/" + Format(m.MarinelliHoleBase2)
                                  + " × " + Format(m.MarinelliHoleWidth) + " " + Resources.GeometryEditorUnitMm);
                    }

                    break;
            }

            // (`AMBER282`) Защита в расчёте — наружный габарит и число слоёв.
            if (m.ShieldInScene)
            {
                lines.Add(string.Format(CultureInfo.InvariantCulture, Resources.EfficiencySketchShield,
                                        m.ShieldLayers.Count, Format(m.ShieldOuterDiameter), Format(m.ShieldOuterHeight),
                                        Resources.GeometryEditorUnitMm));
            }

            // Табличка обязана поместиться в отведённое ей поле, а поле у
            // миниатюры чужое: её ширину задаёт окно конфигурации прибора.
            // Строка «Точечный источник, дистанция: 100 мм» на 220 точках
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
                          double tfr, double tsr, double tfc, double tsc, double tm,
                          double tfg, double tsg)
        {
            double outerHalf = halfWidth + tsr + tsg + tsc;
            double zFace = -(tfr + tfg + tfc);

            // Слои рисуются снаружи внутрь: корпус целиком, потом зазор, потом
            // отражатель, потом кристалл. Так же они и вложены в сцене расчёта
            // — там побеждает первая область, в которую попала точка.
            Fill(g, CladdingColor, -outerHalf, zFace, 2.0 * outerHalf, height + tm - zFace);
            double gapHalf = halfWidth + tsr + tsg;
            Fill(g, GapColor, -gapHalf, -(tfr + tfg), 2.0 * gapHalf, height + tfr + tfg);
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
                      double tfr, double tsr, double tfc, double tsc, double tm,
                      double tfg, double tsg)
        {
            double outerHalf = halfWidth + tsr + tsg + tsc;
            double zFace = -(tfr + tfg + tfc);

            // (`AMBER201`, Р4, мелочь G.3) Ключи подсветки едут ВМЕСТЕ с
            // толщинами, переставленными вызывающим: размер у грани, обращённой
            // к пробе, при боковой постановке — это поле `Side*`, и подсвечиваться
            // он обязан по нему. Прежде числа переставлялись, а ключи нет: фокус
            // в поле «боковой отражатель» подсвечивал торцевой размер с другим
            // числом — чертёж показывал одно, а редактор правил другое.
            string kfr = "FrontReflectorThickness", ksr = "SideReflectorThickness";
            string kfc = "FrontCladdingThickness", ksc = "SideCladdingThickness";
            string kfg = "FrontGapThickness", ksg = "SideGapThickness";
            if (m.Facing == GeometryDetectorFacing.Side)
            {
                string kt = kfr; kfr = ksr; ksr = kt;
                kt = kfc; kfc = ksc; ksc = kt;
                kt = kfg; kfg = ksg; ksg = kt;
            }

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
                this.DimV(g, pen, ink, -halfWidth * 0.45, -tfr, 0.0, tfr, kfr);
                this.DimV(g, pen, ink, halfWidth * 0.45, zFace, -(tfr + tfg), tfc, kfc);
                this.DimH(g, pen, ink, -halfWidth - tsr, -halfWidth, height * 0.35, tsr, ksr);
                this.DimH(g, pen, ink, -outerHalf, -halfWidth - tsr - tsg, height * 0.62, tsc, ksc);

                // (`AMBER1`) Размер зазора ставится, ТОЛЬКО когда он есть:
                // нулевая выноска на чертеже — это шум, а не сведение.
                if (tfg > 0.0)
                {
                    this.DimV(g, pen, ink, -halfWidth * 0.75, -(tfr + tfg), -tfr, tfg, kfg);
                }

                if (tsg > 0.0)
                {
                    this.DimH(g, pen, ink, -halfWidth - tsr - tsg, -halfWidth - tsr,
                              height * 0.48, tsg, ksg);
                }
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

        /// <summary>
        /// Окружность, которой на чертеже показано изотропное поле
        /// (`AMBER13` (б)): центр и радиус в мм сцены. НЕ В МАСШТАБЕ нарочно —
        /// сфера поля в полметра свела бы детектор в точку, а её радиус
        /// ответа не меняет; окружность берётся чуть шире габарита детектора
        /// с оправой, число же стоит подписью.
        /// </summary>
        static void FieldRing(GeometryModel m, double zFace, out double centerZ, out double radius)
        {
            double depth;
            if (m.Shape == CrystalShape.Box)
            {
                double hx, hy;
                m.CrystalBoxInScene(out hx, out hy, out depth);
            }
            else
            {
                depth = m.CrystalHeight;
            }

            double zBack = Math.Max(depth, 0.0) + Math.Max(m.MountingThickness, 0.0);
            double halfLength = 0.5 * (zBack - zFace);
            double halfWidth = 0.5 * GeometryScenes.DetectorOuterDiameterMm(m);
            centerZ = 0.5 * (zFace + zBack);
            radius = 1.35 * Math.Sqrt(halfWidth * halfWidth + halfLength * halfLength);
        }

        /// <remarks>(`AMBER282`) Статический и открытый сборке: тем же габаритом
        /// <see cref="GeometryScenes.SceneEnvelopeMm"/> кладёт полость защиты, и чертёж с проверкой
        /// не разойдутся.</remarks>
        internal static void SourceBounds(GeometryModel m, double zFace,
                          out double left, out double right, out double top, out double bottom)
        {
            // (`AMBER13` (б)) Поле: кадр — окружность вокруг детектора.
            if (m.Scene == GeometrySceneKind.Iso)
            {
                double zc, rr;
                FieldRing(m, zFace, out zc, out rr);
                left = -rr;
                right = rr;
                top = zc - rr;
                bottom = zc + rr;
                return;
            }

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
                    // (`E42`) Границы обязаны НАКРЫВАТЬ то, что рисует
                    // DrawSource, иначе тело уезжает за край поля — тот же
                    // договор, что записан у маринелли ниже и соблюдён у
                    // бруска выше.
                    //
                    // ⛔ ВЕРХ ТЕЛА У ЦИЛИНДРА — `zTop - end - hs`, и `BeakerHeight`
                    // в нём не участвует: `DrawSource` рисует стенку высотой
                    // `hs + end` от `zSrcTop - hs`, а полной высоты сосуда не
                    // читает вовсе. Здесь же стояло `zTop - BeakerHeight - hs`,
                    // и при `BeakerHeight < BeakerEndWallThickness` кадр
                    // оказывался НИЖЕ нарисованного тела.
                    //
                    // ⚠ Взят `Math.Max`, а не одно только `end`. Кадр обязан
                    // накрывать нарисованное — это и чинится; делать его ещё и
                    // ПЛОТНЫМ значит сдвинуть кадр КАЖДОГО чертежа цилиндра
                    // (на штатной сцене «на земле» `BeakerHeight = SourceHeight`
                    // при нулевом донышке, то есть высота кадра завышена вдвое),
                    // а дефекта, который бы это оправдывал, нет: масштаб там
                    // задаёт ширина. Отдельное решение, отдельная строка.
                    double rOut = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                    double zTop = zFace - Math.Max(m.BeakerToDetectorDistance, 0.0);
                    // (`f8cyl`) Дальнее дно рисуется за пробой: кадр накрывает и его (у
                    // сосуда без дна — то же число, что прежде).
                    double reserve = Math.Max(Math.Max(Math.Max(m.BeakerHeight, 0.0),
                                                       Math.Max(m.BeakerEndWallThickness, 0.0)),
                                              Math.Max(m.BeakerEndWallThickness, 0.0) + CylinderFarEnd(m));
                    left = -rOut;
                    right = rOut;
                    top = zTop - reserve - Math.Max(m.SourceHeight, 0.0);
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
                    // (`AMBER205`) Пустая часть стакана — за пробой (см. DrawSource).
                    double zTopOuter = zSrc0 - endWall - MarinelliFarGap(m);
                    left = -rOut;
                    right = rOut;
                    top = zTopOuter;
                    bottom = Math.Max(zTopOuter + Math.Max(m.MarinelliBeakerHeight, 0.0), zFace);
                    return;
                }
            }
        }

        /// <summary>
        /// (`AMBER205`) Пустота стакана маринелли между пробой и дальним
        /// донышком, мм — тем же правилом, каким строится сцена счёта
        /// (<see cref="EfficiencySimulator.MarinelliVessel"/>): чертёж обязан
        /// рисовать то, что считается.
        /// </summary>
        static double MarinelliFarGap(GeometryModel m)
        {
            double farGap, nearExt, endWall;
            EfficiencySimulator.MarinelliVessel(m, out farGap, out nearExt, out endWall);
            return farGap;
        }

        /// <summary>
        /// (`AMBER205`) Запас боковой стенки цилиндра над пробой, мм —
        /// <see cref="EfficiencySimulator.CylinderVessel"/>.
        /// </summary>
        static double CylinderHeadspace(GeometryModel m)
        {
            double headspace;
            EfficiencySimulator.CylinderVessel(m, out headspace);
            return headspace;
        }

        /// <summary>
        /// (`f8cyl`) Дальнее дно цилиндра, мм, той же раскладкой, что сцена счёта
        /// (<see cref="EfficiencySimulator.CylinderVessel(GeometryModel, out double, out double)"/>):
        /// прежде чертёж обводил дальний край пробы линией, будто дно там есть, а в перенос
        /// оно не входило.
        /// </summary>
        static double CylinderFarEnd(GeometryModel m)
        {
            double headspace, farEnd;
            EfficiencySimulator.CylinderVessel(m, out headspace, out farEnd);
            return farEnd;
        }

        void DrawSource(Graphics g, GeometryModel m, double zFace)
        {
            // (`AMBER13` (б)) Изотропное поле: штриховая окружность вокруг
            // детектора и восемь стрелок ВНУТРЬ — квант приходит со всех
            // сторон. Окружность не в масштабе (см. FieldRing).
            if (m.Scene == GeometrySceneKind.Iso)
            {
                double zc, rr;
                FieldRing(m, zFace, out zc, out rr);
                float cx = this.X(0.0), cy = this.Y(zc), pr = this.L(rr);
                using (Pen ring = new Pen(SampleColor, 1.2f) { DashStyle = DashStyle.Dash })
                {
                    g.DrawEllipse(ring, cx - pr, cy - pr, 2f * pr, 2f * pr);
                }

                using (Pen arrow = new Pen(SampleColor, 1.4f))
                {
                    arrow.CustomEndCap = new System.Drawing.Drawing2D.AdjustableArrowCap(4f, 6f);
                    for (int k = 0; k < 8; k++)
                    {
                        double a = k * Math.PI / 4.0;
                        float ex = (float)Math.Cos(a), ey = (float)Math.Sin(a);
                        g.DrawLine(arrow, cx + ex * pr, cy + ey * pr,
                                   cx + ex * pr * 0.72f, cy + ey * pr * 0.72f);
                    }
                }

                return;
            }

            // (`AMBER282`) Защита в расчёте — первой, под всем остальным.
            this.DrawShield(g, m);

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
                    // (`AMBER205`) Стенка — на полную высоту сосуда, запас над
                    // пробой пуст (крышки нет) — той же раскладкой, что сцена
                    // счёта (`EfficiencySimulator.CylinderVessel`).
                    double headspace = CylinderHeadspace(m);
                    // (`f8cyl`) Дальнее дно — цветом стенки за запасом (у сосуда без дна — ноль).
                    double farEnd = CylinderFarEnd(m);
                    double zBody = zSrcTop - hs - headspace - farEnd;
                    Fill(g, WallColor, -rOut, zBody, 2.0 * rOut, hs + end + headspace + farEnd);
                    if (headspace > 0.0)
                    {
                        Fill(g, Canvas, -(rOut - wall), zBody + farEnd, 2.0 * (rOut - wall), headspace);
                    }

                    Fill(g, SampleColor, -(rOut - wall), zSrcTop - hs, 2.0 * (rOut - wall), hs);
                    using (Pen pen = new Pen(Ink, 1.2f))
                    {
                        Outline(g, pen, -rOut, zBody, 2.0 * rOut, hs + end + headspace + farEnd);
                    }

                    return;
                }

                default:
                {
                    // Стакан Маринелли: проба охватывает детектор, колодец
                    // открыт снизу — детектор входит в него.
                    double rOut = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                    // (`AMBER282`) У трапеции — полуширина по середине её высоты: разрез идёт по оси x.
                    double rh, rhOut;
                    HoleHalfWidths(m, out rh, out rhOut);
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
                    //
                    // (`AMBER205`) Пустая часть стакана — ЗА пробой, со стороны,
                    // дальней от детектора (`EfficiencySimulator.MarinelliVessel`,
                    // ей же строится сцена счёта): верх тела уходит на `farGap`
                    // дальше донышка-вплотную, и пустота рисуется пустотой.
                    double farGap = MarinelliFarGap(m);
                    double zTopOuter = zSrc0 - endWall - farGap;
                    Fill(g, WallColor, -rOut, zTopOuter, 2.0 * rOut, body);
                    double rSrcOut = Math.Max(rhOut, rOut - side);
                    if (farGap > 0.0)
                    {
                        Fill(g, Canvas, -rSrcOut, zTopOuter + endWall, 2.0 * rSrcOut, farGap);
                    }

                    Fill(g, SampleColor, -rSrcOut, zSrc0, 2.0 * rSrcOut, hs);
                    // колодец: вырез в пробе, стенка колодца и пустота внутри
                    Fill(g, WallColor, -rhOut, zCeiling - the, 2.0 * rhOut, the + hh);
                    Fill(g, Canvas, -rh, zCeiling, 2.0 * rh, hh);

                    using (Pen pen = new Pen(Ink, 1.2f))
                    {
                        Outline(g, pen, -rOut, zTopOuter, 2.0 * rOut, body);
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
                // (`AMBER13` (б)) Поле: единственный размер — радиус сферы, и
                // он подписан числом над окружностью, а не размерной линией:
                // окружность не в масштабе, и линия по ней лгала бы.
                if (m.Scene == GeometrySceneKind.Iso)
                {
                    double zc, rr;
                    FieldRing(m, zFace, out zc, out rr);
                    string text = "R = " + Format(Math.Max(m.FieldRadius, 0.0));
                    SizeF size = g.MeasureString(text, this.Font);
                    bool lit = this.Lit("FieldRadius");
                    using (Brush litInk = lit ? new SolidBrush(LitColor) : null)
                    {
                        // Через Print, а не голым DrawString (П329): подпись мимо
                        // него в `Labels` не попадала, и у сцены ISO приёмка
                        // чертежа видела ноль подписей.
                        this.Print(g, lit ? litInk : ink, text, "FieldRadius",
                                   this.X(0.0) - size.Width / 2f, this.Y(zc - rr) - size.Height - 2f, size);
                    }

                    return;
                }

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
                        this.SampleHeightDim(g, pen, ink, half, zSrcTopB - hsB, zSrcTopB, hsB, "BoxSourceHeight",
                                             this.RightOf(half, 26));
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
                        // (`AMBER282`) Есть вид сверху — сторона Y стоит на нём размером; надпись
                        // «Y = …» у тела в защите вытеснялась полосами внутрь пробы.
                        if (!this.planView)
                        {
                            this.Note(g, ink, m.BoxSourceY, -half, zSrcTopB - hsB * 0.5,
                                      -6f, "BoxSourceY", -8f);
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
                        this.DimH(g, pen, ink, -rOut, rOut, this.AboveTop(22),
                                  2.0 * rOut, "BeakerDiameter");
                        this.SampleHeightDim(g, pen, ink, rOut, zSrcTop - hs, zSrcTop, hs, "SourceHeight",
                                             this.RightOf(rOut, 26));
                        this.DimV(g, pen, ink, 0.0, zWallTop, zFace,
                                  Math.Max(m.BeakerToDetectorDistance, 0.0), "BeakerToDetectorDistance");
                        this.DimV(g, pen, ink, -rOut * 0.55, zSrcTop, zWallTop, end, "BeakerEndWallThickness");
                        this.DimH(g, pen, ink, -rOut, -(rOut - wall), zSrcTop - hs * 0.5,
                                  wall, "BeakerSideWallThickness");
                        // ⛔ (`E39`) ВЫСОТА СОСУДА ПОДПИСЫВАЕТСЯ ТОЛЬКО ТАМ, ГДЕ
                        // СОСУД ЕСТЬ. Сцена «на земле» ставит `BeakerHeight =
                        // SourceHeight` (поле уезжает в `.in`, пусть не врёт), и
                        // чертёж честно печатал два одинаковых числа в трёх
                        // точках друг от друга: в колонке редактора шириной 232
                        // точки «638.6» и «638.6» читаются как одно число из
                        // одиннадцати знаков.
                        //
                        // ⚠ Судим НЕ по совпадению значений: чертёж не вправе
                        // решать, что размер сосуда совпал с размером пробы не
                        // случайно, — человек мог ввести равные числа руками, и
                        // тогда оба размера обязаны стоять. Судим по СТЕНКАМ:
                        // у сцены «на земле» стенок нет ни боковой, ни донышка
                        // (грунт — не стакан), а у сосуда без стенок высоты нет
                        // как размера, есть только пустое поле модели.
                        double farEnd = CylinderFarEnd(m);
                        if (farEnd > 0.0)
                        {
                            // (`f8cyl`) Дальнее дно — за пробой и запасом.
                            double zFar1 = zSrcTop - hs - CylinderHeadspace(m);
                            this.DimV(g, pen, ink, -rOut * 0.55, zFar1 - farEnd, zFar1, farEnd,
                                      "BeakerFarEndWallThickness");
                        }

                        if (VesselExists(wall, Math.Max(end, farEnd)))
                        {
                            // (`AMBER205`) Сосуд — с запасом стенки над пробой (и дальним дном, `f8cyl`).
                            this.DimV(g, pen, ink, rOut * 0.72, zSrcTop - hs - CylinderHeadspace(m) - farEnd, zWallTop,
                                      Math.Max(m.BeakerHeight, 0.0), "BeakerHeight");
                        }

                        return;
                    }

                    default:
                    {
                        double rOut = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                        double rh, rhOut;
                        HoleHalfWidths(m, out rh, out rhOut);
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
                        // (`AMBER282`) У трапеции диаметра нет — её размеры стоят на виде в плане.
                        if (!m.HasTrapezoidHole)
                        {
                            this.DimH(g, pen, ink, -rh, rh, zCeiling + hh * 0.55, 2.0 * rh,
                                      "MarinelliHoleDiameter");
                        }
                        // 40 точек, а не 30: число DimV пишет СПРАВА от своей
                        // линии, то есть в зазор до стенки стакана. Тридцати
                        // хватало на «5», но не на «12.35» — оно налезало на
                        // тело. В поле (MarginX = 78) сорок помещается.
                        if (this.shieldPx != null)
                        {
                            // (`AMBER282`, дизайн п. 5) В защите высота пробы — слева ЗА
                            // защитой, с выносными линиями от граней пробы.
                            float xl = this.X(-0.5 * Math.Max(m.ShieldInnerDiameter, 0.0)) - this.shieldPx.SideTotal - 14f;
                            using (Pen leader = new Pen(Ink, 0.8f))
                            {
                                g.DrawLine(leader, this.X(-rOut), this.Y(zSrc0), xl - 3f, this.Y(zSrc0));
                                g.DrawLine(leader, this.X(-rOut), this.Y(zSrc0 + hs), xl - 3f, this.Y(zSrc0 + hs));
                            }

                            this.DimVAt(g, pen, ink, xl, this.Y(zSrc0), this.Y(zSrc0 + hs), hs, "MarinelliSourceHeight");
                        }
                        else
                        {
                            this.DimV(g, pen, ink, this.LeftOf(-rOut, 40), zSrc0, zSrc0 + hs, hs,
                                      "MarinelliSourceHeight");
                        }

                        this.DimV(g, pen, ink, rh * 0.55, zCeiling, zCeiling + hh, hh, "MarinelliHoleHeight");
                        // Корпус рисуется от zSrc0 - endWall высотой body
                        // (см. DrawSource), выноска обязана мерить ровно то же и
                        // показывать само поле, а не сумму с чем-нибудь.
                        //
                        // ⛔ (`E39`) ТО ЖЕ ПРАВИЛО, ЧТО У ЦИЛИНДРА. Строка
                        // реестра назвала одну сцену, а их ДВЕ: «в лунке»
                        // (`GeometryScenes.Borehole`) ставит
                        // `MarinelliBeakerHeight = MarinelliSourceHeight` и
                        // обнуляет обе стенки ровно так же, как «на земле», —
                        // и точно так же печатала одно число дважды. Лунка в
                        // грунте стаканом не является, высоты стакана у неё нет.
                        // (`AMBER205`) Верх корпуса — за пустой частью стакана.
                        double zTopOuter = zSrc0 - endWall - MarinelliFarGap(m);
                        if (VesselExists(side, endWall))
                        {
                            this.DimV(g, pen, ink, this.RightOf(rOut, 26), zTopOuter,
                                      zTopOuter + body, body, "MarinelliBeakerHeight");
                        }
                        this.DimH(g, pen, ink, -rOut, -(rOut - side), zSrc0 + hs * 0.28, side,
                                  "MarinelliSideThickness");
                        // Донышко — своя толщина, а не боковая. Раньше здесь
                        // стояло side: поле подсвечивалось, а число показывало
                        // соседний размер.
                        this.DimV(g, pen, ink, -rOut * 0.72, zTopOuter, zTopOuter + endWall, endWall,
                                  "MarinelliEndWallThickness");
                        // Стенки колодца разведены по высоте и по стороне: при
                        // общем 0.2 их подписи иначе налезают друг на друга и
                        // на выноску диаметра колодца.
                        this.DimH(g, pen, ink, rh, rhOut, zCeiling + hh * 0.82, ths,
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
        // (`AMBER282`) Колодец-трапеция и защита в расчёте
        // ------------------------------------------------------------------

        /// <summary>
        /// Полуширина колодца в разрезе — просвет и наружный контур гильзы. У круга
        /// это радиус и радиус плюс стенка (до бита прежние числа чертежа); у
        /// трапеции — на середине её высоты в плане (y = 0): разрез идёт по оси x,
        /// вдоль оснований, и видна их средняя ширина.
        /// </summary>
        static void HoleHalfWidths(GeometryModel m, out double inner, out double outer)
        {
            double ths = Math.Max(m.MarinelliHoleSideThickness, 0.0);
            double[] xs = new double[4], ys = new double[4], xo = new double[4], yo = new double[4];
            if (m.TrapezoidHoleVertices(0.0, xs, ys) && m.TrapezoidHoleVertices(ths, xo, yo))
            {
                inner = 0.5 * (xs[1] + xs[2]);
                outer = 0.5 * (xo[1] + xo[2]);
                return;
            }

            inner = 0.5 * Math.Max(m.MarinelliHoleDiameter, 0.0);
            outer = inner + ths;
        }

        /// <summary>Защита на чертеже: полость в мировых мм (слои рисуются полосами в точках, см. <see cref="ShieldPx"/>).</summary>
        struct ShieldBox
        {
            public double Cavity, FloorFace, LidFace;     // радиус полости, внутренние грани дна и крышки
        }

        /// <summary>
        /// Полость защиты тем же правилом, что у переноса (`EfficiencySimulator.AddShield`):
        /// дно — на <c>ShieldFloorGap</c> дальше дальнего края сцены
        /// (<see cref="GeometryScenes.SceneEnvelopeMm"/>), крышка — на высоте полости от
        /// дна. Ложь — защиты в сцене нет.
        /// </summary>
        static bool ShieldInSketch(GeometryModel m, out ShieldBox box)
        {
            box = new ShieldBox();
            double radius, top, bottom;
            if (m == null || !m.ShieldInScene || !GeometryScenes.SceneEnvelopeMm(m, out radius, out top, out bottom))
            {
                return false;
            }

            box.Cavity = 0.5 * m.ShieldInnerDiameter;
            box.FloorFace = top - Math.Max(0.0, m.ShieldFloorGap);
            box.LidFace = box.FloorFace + m.ShieldInnerHeight;
            return true;
        }

        /// <summary>
        /// (`AMBER282`, предложение дизайнера 03.11.2026 на замечание Amber «защита на
        /// скетче выглядит ужасно») Слои защиты — ПОЛОСАМИ В ТОЧКАХ вокруг полости:
        /// тонкий слой — не меньше <see cref="MinBandPx"/> точек, толстый — не больше
        /// предела (16 точек у поля уже 400, 20 у широкого, 10 у миниатюры) с условным
        /// разрывом по ГОСТ 2.305, если честная толщина выходит за предел больше чем на 4
        /// точки. Честная толщина, которая помещается, рисуется как есть.
        /// </summary>
        sealed class ShieldPx
        {
            public float[] Side, Floor, Lid;
            public bool[] SideBroken, FloorBroken, LidBroken;
            public float SideTotal, FloorTotal, LidTotal;

            /// <summary>Хоть одна полоса не в масштабе (утолщена или с разрывом).</summary>
            public bool NotToScale;
        }

        ShieldPx shieldPx;

        const float MinBandPx = 3f;

        float BandCapPx
        {
            get { return this.Mode == SketchMode.Overview ? 10f : (this.Width < 400 ? 16f : 20f); }
        }

        /// <summary>Полосы слоёв при масштабе <paramref name="scale"/>; 0 — масштаба ещё нет, берётся предел (оценка сверху).</summary>
        ShieldPx BandsPx(GeometryModel m, double scale)
        {
            int n = m.ShieldLayers.Count;
            ShieldPx px = new ShieldPx
            {
                Side = new float[n], Floor = new float[n], Lid = new float[n],
                SideBroken = new bool[n], FloorBroken = new bool[n], LidBroken = new bool[n],
            };
            float cap = this.BandCapPx;
            for (int i = 0; i < n; i++)
            {
                GeometryShieldLayer layer = m.ShieldLayers[i];
                bool broken, thick;
                px.Side[i] = Band(layer.SideThickness, scale, cap, out broken, out thick);
                px.SideBroken[i] = broken;
                px.NotToScale |= broken || thick;
                px.Floor[i] = Band(layer.FloorThickness, scale, cap, out broken, out thick);
                px.FloorBroken[i] = broken;
                px.NotToScale |= broken || thick;
                px.Lid[i] = Band(layer.LidThickness, scale, cap, out broken, out thick);
                px.LidBroken[i] = broken;
                px.NotToScale |= broken || thick;
                px.SideTotal += px.Side[i];
                px.FloorTotal += px.Floor[i];
                px.LidTotal += px.Lid[i];
            }

            return px;
        }

        static float Band(double thickness, double scale, float cap, out bool broken, out bool thickened)
        {
            broken = false;
            thickened = false;
            if (!(thickness > 0.0))
            {
                return 0f;
            }

            if (!(scale > 0.0))
            {
                return cap;
            }

            double honest = thickness * scale;
            if (honest > cap + 4.0)
            {
                broken = true;
                return cap;
            }

            if (honest < MinBandPx)
            {
                thickened = true;
                return MinBandPx;
            }

            return (float)honest;
        }

        /// <summary>Наружный контур слоя <paramref name="layer"/> (−1 — полость) на экране.</summary>
        RectangleF ShieldRect(ShieldBox box, int layer)
        {
            float left = this.X(-box.Cavity), right = this.X(box.Cavity);
            float floor = this.FloorEdge(box, layer), lid = this.LidEdge(box, layer);
            for (int i = 0; i <= layer; i++)
            {
                left -= this.shieldPx.Side[i];
                right += this.shieldPx.Side[i];
            }

            return RectangleF.FromLTRB(left, Math.Min(floor, lid), right, Math.Max(floor, lid));
        }

        /// <summary>Наружная грань дна слоя <paramref name="layer"/> (−1 — полость) на экране: вверху, а у отражённого чертежа — внизу.</summary>
        float FloorEdge(ShieldBox box, int layer)
        {
            float y = this.Y(box.FloorFace), d = 0f;
            for (int i = 0; i <= layer; i++)
            {
                d += this.shieldPx.Floor[i];
            }

            return this.flipZ ? y + d : y - d;
        }

        /// <summary>Наружная грань крышки слоя на экране.</summary>
        float LidEdge(ShieldBox box, int layer)
        {
            float y = this.Y(box.LidFace), d = 0f;
            for (int i = 0; i <= layer; i++)
            {
                d += this.shieldPx.Lid[i];
            }

            return this.flipZ ? y - d : y + d;
        }

        /// <summary>Тяжёлый ли слой (сталь, свинец): у него штриховка, у лёгкого — сплошная светлая заливка.</summary>
        static bool HeavyLayer(GeometryMaterial material)
        {
            return material != null && material.Density >= 5.0;
        }

        /// <summary>
        /// Цвет слоя по плотности (дизайн, п. 1): лёгкий металл — #E6EAEE, светлее фона;
        /// тяжёлый — тёплый серый от #B0ABA0 (ρ 5) до #8C8880 (ρ ≥ 11.3), не путается с
        /// холодной пробой #78808E. Тёмного варианта нет: чертёж «бумажный» на своём фоне.
        /// </summary>
        static Color ShieldColor(GeometryMaterial material)
        {
            if (!HeavyLayer(material))
            {
                return Color.FromArgb(0xE6, 0xEA, 0xEE);
            }

            double t = Math.Max(0.0, Math.Min(1.0, (material.Density - 5.0) / (11.3 - 5.0)));
            Func<int, int, int> mix = (a, b) => (int)Math.Round(a + (b - a) * t);
            return Color.FromArgb(mix(0xB0, 0x8C), mix(0xAB, 0x88), mix(0xA0, 0x80));
        }

        static readonly Color ShieldSeam = Color.FromArgb(0x4A, 0x47, 0x40);

        void DrawShield(Graphics g, GeometryModel m)
        {
            ShieldBox box;
            if (this.shieldPx == null || !ShieldInSketch(m, out box))
            {
                return;
            }

            int n = m.ShieldLayers.Count;
            RectangleF cavity = this.ShieldRect(box, -1);

            // Снаружи внутрь: каждый слой — сплошной прямоугольник своего наружного
            // контура, следующий внутренний ложится поверх.
            for (int i = n - 1; i >= 0; i--)
            {
                RectangleF outer = this.ShieldRect(box, i);
                Color fill = ShieldColor(m.ShieldLayers[i].Material);
                using (Brush brush = new SolidBrush(fill))
                {
                    g.FillRectangle(brush, outer);
                }

                if (HeavyLayer(m.ShieldLayers[i].Material))
                {
                    // Штриховка линиями под 45°, 1 точка, шаг 7 — не HatchBrush: его
                    // сетка 8×8 грубая и спорит с палитрой.
                    GraphicsState state = g.Save();
                    g.SetClip(outer);
                    using (Pen hatch = new Pen(Color.FromArgb(fill.R * 82 / 100, fill.G * 82 / 100, fill.B * 82 / 100), 1f))
                    {
                        for (float d = -outer.Height; d < outer.Width; d += 7f)
                        {
                            g.DrawLine(hatch, outer.Left + d, outer.Bottom, outer.Left + d + outer.Height, outer.Top);
                        }
                    }

                    g.Restore(state);
                }

                RectangleF inner = this.ShieldRect(box, i - 1);
                this.obstacles.Add(RectangleF.FromLTRB(outer.Left, outer.Top, outer.Right, inner.Top));
                this.obstacles.Add(RectangleF.FromLTRB(outer.Left, inner.Bottom, outer.Right, outer.Bottom));
                this.obstacles.Add(RectangleF.FromLTRB(outer.Left, inner.Top, inner.Left, inner.Bottom));
                this.obstacles.Add(RectangleF.FromLTRB(inner.Right, inner.Top, outer.Right, inner.Bottom));
            }

            using (Brush canvas = new SolidBrush(Canvas))
            {
                g.FillRectangle(canvas, cavity);
            }

            using (Pen seam = new Pen(ShieldSeam, 1f))
            using (Pen ink = new Pen(Ink, 1.2f))
            {
                // Швы между слоями, затем условные разрывы укороченных полос.
                for (int i = 0; i < n - 1; i++)
                {
                    RectangleF r = this.ShieldRect(box, i);
                    g.DrawRectangle(seam, r.X, r.Y, r.Width, r.Height);
                }

                for (int i = 0; i < n; i++)
                {
                    RectangleF outer = this.ShieldRect(box, i), inner = this.ShieldRect(box, i - 1);
                    if (this.shieldPx.SideBroken[i])
                    {
                        float xl = 0.5f * (outer.Left + inner.Left), xr = 0.5f * (outer.Right + inner.Right);
                        ZigZag(g, seam, false, outer.Top, outer.Bottom, xl);
                        ZigZag(g, seam, false, outer.Top, outer.Bottom, xr);
                    }

                    if (this.shieldPx.FloorBroken[i])
                    {
                        ZigZag(g, seam, true, outer.Left, outer.Right, 0.5f * (this.FloorEdge(box, i) + this.FloorEdge(box, i - 1)));
                    }

                    if (this.shieldPx.LidBroken[i])
                    {
                        ZigZag(g, seam, true, outer.Left, outer.Right, 0.5f * (this.LidEdge(box, i) + this.LidEdge(box, i - 1)));
                    }
                }

                RectangleF all = this.ShieldRect(box, n - 1);
                g.DrawRectangle(ink, all.X, all.Y, all.Width, all.Height);
                g.DrawRectangle(ink, cavity.X, cavity.Y, cavity.Width, cavity.Height);

                // Разрыв воздуха полости — две линии поперёк всей защиты.
                if (this.breakPx > 0f)
                {
                    float y0 = Math.Min(this.Y(this.breakZ0), this.Y(this.breakZ1));
                    ZigZag(g, seam, true, all.Left, all.Right, y0 + 5f);
                    ZigZag(g, seam, true, all.Left, all.Right, y0 + this.breakPx - 5f);
                }
            }
        }

        /// <summary>
        /// Линия обрыва по ГОСТ 2.305: тонкая сплошная с изломами (амплитуда ±4, ширина
        /// 8 точек) через каждые 70 точек, у короткой — один излом посередине.
        /// </summary>
        static void ZigZag(Graphics g, Pen pen, bool horizontal, float a, float b, float at)
        {
            if (!(b - a > 12f))
            {
                return;
            }

            List<PointF> points = new List<PointF>();
            Func<float, float, PointF> P = (along, across) => horizontal ? new PointF(along, at + across) : new PointF(at + across, along);
            points.Add(P(a, 0f));
            int breaks = Math.Max(1, (int)((b - a) / 70f));
            float step = (b - a) / breaks;
            for (int k = 0; k < breaks; k++)
            {
                float c = a + step * (k + 0.5f);
                points.Add(P(c - 4f, 0f));
                points.Add(P(c - 2f, 4f));
                points.Add(P(c + 2f, -4f));
                points.Add(P(c + 4f, 0f));
            }

            points.Add(P(b, 0f));
            g.DrawLines(pen, points.ToArray());
        }

        /// <summary>
        /// Размеры защиты (дизайн, п. 4): на эскизе — диаметр полости снизу, её высота
        /// справа с выносными линиями, зазор до дна, если он есть. Толщины слоёв — в
        /// легенде; на эскизе толщина появляется, только когда её поле в фокусе.
        /// </summary>
        void AnnotateShield(Graphics g, GeometryModel m)
        {
            ShieldBox box;
            if (this.shieldPx == null || !ShieldInSketch(m, out box))
            {
                return;
            }

            int n = m.ShieldLayers.Count;
            RectangleF cavity = this.ShieldRect(box, -1), all = this.ShieldRect(box, n - 1);
            using (Pen pen = new Pen(Ink, 1f))
            using (Pen leader = new Pen(Ink, 0.8f))
            using (Brush ink = new SolidBrush(Ink))
            {
                this.DimHAt(g, pen, ink, cavity.Left, cavity.Right, all.Bottom + 14f, m.ShieldInnerDiameter, "ShieldInnerDiameter");

                float xh = all.Right + 14f;
                g.DrawLine(leader, cavity.Right, cavity.Top, xh + 3f, cavity.Top);
                g.DrawLine(leader, cavity.Right, cavity.Bottom, xh + 3f, cavity.Bottom);
                this.DimVAt(g, pen, ink, xh, cavity.Top, cavity.Bottom, m.ShieldInnerHeight, "ShieldInnerHeight");

                // (`AMBER282`, Amber 03.11.2026: «Ошибка в эскизе, внутренняя высота 100, внешняя
                // 160») Как на чертеже — обе высоты: внутренняя (полость, линией выше) и
                // НАРУЖНАЯ целиком — все слои низа, полость и все слои верха. Число настоящее,
                // даже если полосы укорочены разрывом.
                double floorSum = 0.0, lidSum = 0.0;
                foreach (GeometryShieldLayer layer in m.ShieldLayers)
                {
                    floorSum += Math.Max(0.0, layer.FloorThickness);
                    lidSum += Math.Max(0.0, layer.LidThickness);
                }

                float xk = all.Right + 40f;
                float yFloorOuter = this.FloorEdge(box, n - 1), yLidOuter = this.LidEdge(box, n - 1);
                g.DrawLine(leader, all.Right, yFloorOuter, xk + 3f, yFloorOuter);
                g.DrawLine(leader, all.Right, yLidOuter, xk + 3f, yLidOuter);
                this.DimVAt(g, pen, ink, xk, yFloorOuter, yLidOuter, floorSum + m.ShieldInnerHeight + lidSum, null);

                if (m.ShieldFloorGap > 0.0)
                {
                    this.DimV(g, pen, ink, -box.Cavity * 0.85, box.FloorFace, box.FloorFace + m.ShieldFloorGap,
                              m.ShieldFloorGap, "ShieldFloorGap");
                }

                for (int i = 0; i < n; i++)
                {
                    RectangleF outer = this.ShieldRect(box, i), inner = this.ShieldRect(box, i - 1);
                    GeometryShieldLayer layer = m.ShieldLayers[i];
                    string side = GeometryScenes.ShieldLayerField(i, "Side");
                    string floor = GeometryScenes.ShieldLayerField(i, "Floor");
                    string lid = GeometryScenes.ShieldLayerField(i, "Lid");
                    if (this.Lit(side))
                    {
                        this.DimHAt(g, pen, ink, inner.Right, outer.Right, 0.5f * (cavity.Top + cavity.Bottom), layer.SideThickness, side);
                    }

                    if (this.Lit(floor))
                    {
                        this.DimVAt(g, pen, ink, 0.5f * (cavity.Left + cavity.Right), this.FloorEdge(box, i), this.FloorEdge(box, i - 1),
                                    layer.FloorThickness, floor);
                    }

                    if (this.Lit(lid))
                    {
                        this.DimVAt(g, pen, ink, 0.5f * (cavity.Left + cavity.Right), this.LidEdge(box, i - 1), this.LidEdge(box, i),
                                    layer.LidThickness, lid);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // (`AMBER282`) Боковой блок: легенда слоёв защиты и вид в плане колодца
        // ------------------------------------------------------------------

        /// <summary>Строка легенды: образец цвета (пусто — строка без образца), текст, ключ подсветки.</summary>
        sealed class LegendLine
        {
            public GeometryMaterial Swatch;
            public string Text;
            public int Layer = -1;
            public bool Gray;

            /// <summary>Строка наружного габарита: к ней приписывается «не в масштабе».</summary>
            public bool Outer;
        }

        readonly List<LegendLine> legend = new List<LegendLine>();
        readonly List<RectangleF> obstacles = new List<RectangleF>();
        RectangleF sideBlock, legendRect, insetRect;
        bool sideBlockRight;
        float legendLineHeight;

        static string LayerThickness(GeometryShieldLayer layer)
        {
            if (layer.SideThickness == layer.FloorThickness && layer.SideThickness == layer.LidThickness)
            {
                return Format(layer.SideThickness);
            }

            return Format(layer.SideThickness) + " / " + Format(layer.FloorThickness) + " / " + Format(layer.LidThickness)
;
        }

        static string ShortName(GeometryMaterial material)
        {
            if (material == null || string.IsNullOrEmpty(material.Name))
            {
                return "?";
            }

            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(material.Name);
            return entry != null && !string.IsNullOrEmpty(entry.Abbr) ? entry.Abbr : material.Name;
        }

        /// <summary>
        /// Разложить боковой блок (дизайн, пп. 4 и 6): у высокого поля — снизу во всю
        /// ширину (легенда, под ней полоса вида в плане 110 точек), у широкого — колонкой
        /// справа (легенда, под ней квадрат 180). Пустой — ни защиты, ни трапеции.
        /// </summary>
        void LayoutSideBlock(Graphics g, GeometryModel m, bool hasShield)
        {
            this.legend.Clear();
            this.sideBlock = this.legendRect = this.insetRect = RectangleF.Empty;
            this.sideBlockRight = this.Width >= this.Height;
            if (this.Mode != SketchMode.Source)
            {
                return;
            }

            float legendW = 0f, legendH = 0f;
            if (hasShield)
            {
                this.legend.Add(new LegendLine { Text = Resources.GeometryEditorShieldLayers });

                // Разные бок/низ/верх — подсказка порядка своей строкой, а не хвостом у
                // каждого слоя: в колонке 280 точек хвост уходил за край (мерка 03.11.2026).
                bool parts = false;
                foreach (GeometryShieldLayer layer in m.ShieldLayers)
                {
                    parts |= !(layer.SideThickness == layer.FloorThickness && layer.SideThickness == layer.LidThickness);
                }

                if (parts)
                {
                    this.legend.Add(new LegendLine
                    {
                        Text = "(" + Resources.EfficiencySketchShieldParts + ", " + Resources.GeometryEditorUnitMm + ")",
                        Gray = true,
                    });
                }
                for (int i = 0; i < m.ShieldLayers.Count; i++)
                {
                    GeometryShieldLayer layer = m.ShieldLayers[i];
                    this.legend.Add(new LegendLine
                    {
                        Swatch = layer.Material ?? new GeometryMaterial(),
                        Text = (i + 1).ToString(CultureInfo.InvariantCulture) + " " + ShortName(layer.Material) + " — "
                               + LayerThickness(layer) + (parts ? "" : " " + Resources.GeometryEditorUnitMm),
                        Layer = i,
                    });
                }

                this.legend.Add(new LegendLine
                {
                    Text = string.Format(CultureInfo.InvariantCulture, Resources.EfficiencySketchShieldOuter,
                                         Format(m.ShieldOuterDiameter), Format(m.ShieldOuterHeight), Resources.GeometryEditorUnitMm),
                    Gray = true,
                    Outer = true,
                });

                this.legendLineHeight = 0f;
                foreach (LegendLine line in this.legend)
                {
                    string text = line.Outer ? line.Text + Resources.EfficiencySketchNotToScale : line.Text;
                    SizeF size = g.MeasureString(text, this.Font);
                    legendW = Math.Max(legendW, size.Width + (line.Swatch != null ? 16f : 0f));
                    this.legendLineHeight = Math.Max(this.legendLineHeight, size.Height);
                }

                legendW += 12f;
                legendH = this.legendLineHeight * this.legend.Count + 8f;
            }

            if (legendH <= 0f)
            {
                return;
            }

            // Легенда — полосой снизу (Amber 03.11.2026: виды сбоку и сверху занимают
            // поле, колонки справа больше нет).
            this.legendRect = new RectangleF(6f, this.Height - legendH - 6f, Math.Min(legendW, this.Width - 12f), legendH);
            this.sideBlock = this.legendRect;
            this.sideBlockRight = false;

            // Числа разреза в легенду не садятся.
            this.obstacles.Add(this.legendRect);
        }

        void DrawShieldLegend(Graphics g, GeometryModel m)
        {
            if (this.legendRect.IsEmpty)
            {
                return;
            }

            using (Brush plate = new SolidBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF)))
            using (Pen frame = new Pen(Color.FromArgb(0x90, 0x90, 0x90)))
            using (Brush ink = new SolidBrush(Ink))
            using (Brush gray = new SolidBrush(Color.FromArgb(0x6A, 0x66, 0x5E)))
            using (Brush lit = new SolidBrush(LitColor))
            using (Pen seam = new Pen(ShieldSeam, 1f))
            {
                RectangleF r = this.legendRect;
                g.FillRectangle(plate, r);
                g.DrawRectangle(frame, r.X, r.Y, r.Width, r.Height);
                float y = r.Y + 4f;
                foreach (LegendLine line in this.legend)
                {
                    float x = r.X + 6f;
                    if (line.Swatch != null)
                    {
                        RectangleF sw = new RectangleF(x, y + 0.5f * (this.legendLineHeight - 10f), 10f, 10f);
                        Color fill = ShieldColor(line.Swatch);
                        using (Brush b = new SolidBrush(fill))
                        {
                            g.FillRectangle(b, sw);
                        }

                        if (HeavyLayer(line.Swatch))
                        {
                            using (Pen hatch = new Pen(Color.FromArgb(fill.R * 82 / 100, fill.G * 82 / 100, fill.B * 82 / 100), 1f))
                            {
                                g.DrawLine(hatch, sw.Left, sw.Bottom, sw.Right, sw.Top);
                            }
                        }

                        g.DrawRectangle(seam, sw.X, sw.Y, sw.Width, sw.Height);
                        x += 16f;
                    }

                    bool isLit = line.Layer >= 0
                                 && (this.Lit(GeometryScenes.ShieldLayerField(line.Layer, "Side"))
                                     || this.Lit(GeometryScenes.ShieldLayerField(line.Layer, "Floor"))
                                     || this.Lit(GeometryScenes.ShieldLayerField(line.Layer, "Lid"))
                                     || this.Lit(GeometryScenes.ShieldLayerField(line.Layer, "Density")));
                    string text = line.Outer && (this.shieldPx != null && this.shieldPx.NotToScale || this.breakPx > 0f)
                        ? line.Text + Resources.EfficiencySketchNotToScale : line.Text;
                    this.Print(g, isLit ? lit : (line.Gray ? gray : ink), text, null, x, y, g.MeasureString(text, this.Font));
                    y += this.legendLineHeight;
                }
            }
        }

        // ------------------------------------------------------------------
        // (`AMBER282`) Вид сверху — план сцены целиком: защита, полость, проба,
        // колодец, прибор. Тем же масштабом, что вид сбоку, и тем же языком:
        // заливки и штриховка слоёв, тонкий слой — не тоньше MinBandPx.
        // ------------------------------------------------------------------

        bool planView, planStacked;
        double planRadius;
        PointF planCenter, planTitleAt;

        /// <summary>Подписи вида сверху — на светлой плашке: числа стоят на тёмной пробе.</summary>
        bool plateLabels;

        float PX(double x)
        {
            return (float)(this.planCenter.X + x * this.scale);
        }

        float PY(double y)
        {
            return (float)(this.planCenter.Y + y * this.scale);
        }

        /// <summary>Подписи «Вид сбоку» и «Вид сверху» — до размеров: раскладка чисел их обходит.</summary>
        void DrawViewTitles(Graphics g, float volumePlateRight)
        {
            if (!this.planView)
            {
                return;
            }

            using (Brush ink = new SolidBrush(Ink))
            using (Font bold = new Font(this.Font, FontStyle.Bold))
            {
                SizeF a = g.MeasureString(Resources.EfficiencySketchSideView, bold);
                this.Print(g, bold, ink, Resources.EfficiencySketchSideView, null,
                           volumePlateRight > 0f ? volumePlateRight + 8f : 8f, 6f, a);
                SizeF b = g.MeasureString(Resources.EfficiencySketchTopView, bold);
                float x = this.planStacked ? 8f : Math.Max(this.planTitleAt.X, volumePlateRight + a.Width + 24f);
                this.Print(g, bold, ink, Resources.EfficiencySketchTopView, null, x, this.planTitleAt.Y, b);
            }
        }

        void FillCircle(Graphics g, Color color, float r)
        {
            if (!(r > 0f))
            {
                return;
            }

            using (Brush brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, this.planCenter.X - r, this.planCenter.Y - r, 2f * r, 2f * r);
            }
        }

        void Circle(Graphics g, Pen pen, float r)
        {
            if (r > 0f)
            {
                g.DrawEllipse(pen, this.planCenter.X - r, this.planCenter.Y - r, 2f * r, 2f * r);
            }
        }

        void DrawPlanView(Graphics g, GeometryModel m)
        {
            if (!this.planView)
            {
                return;
            }

            ShieldBox box;
            bool shield = this.shieldPx != null && ShieldInSketch(m, out box);
            float cavityPx = shield ? (float)(0.5 * m.ShieldInnerDiameter * this.scale) : 0f;
            float outerPx = cavityPx;

            using (Pen ink = new Pen(Ink, 1.2f))
            using (Pen seam = new Pen(ShieldSeam, 1f))
            {
                // 1. Кольца защиты снаружи внутрь, затем полость.
                if (shield)
                {
                    int n = m.ShieldLayers.Count;
                    float[] radii = new float[n];
                    float r = cavityPx;
                    for (int i = 0; i < n; i++)
                    {
                        r += this.shieldPx.Side[i];
                        radii[i] = r;
                    }

                    outerPx = r;
                    for (int i = n - 1; i >= 0; i--)
                    {
                        Color fill = ShieldColor(m.ShieldLayers[i].Material);
                        this.FillCircle(g, fill, radii[i]);
                        if (HeavyLayer(m.ShieldLayers[i].Material))
                        {
                            using (GraphicsPath path = new GraphicsPath())
                            {
                                path.AddEllipse(this.planCenter.X - radii[i], this.planCenter.Y - radii[i], 2f * radii[i], 2f * radii[i]);
                                GraphicsState state = g.Save();
                                g.SetClip(path);
                                using (Pen hatch = new Pen(Color.FromArgb(fill.R * 82 / 100, fill.G * 82 / 100, fill.B * 82 / 100), 1f))
                                {
                                    float left = this.planCenter.X - radii[i], bottom = this.planCenter.Y + radii[i];
                                    for (float d = -2f * radii[i]; d < 2f * radii[i]; d += 7f)
                                    {
                                        g.DrawLine(hatch, left + d, bottom, left + d + 2f * radii[i], bottom - 2f * radii[i]);
                                    }
                                }

                                g.Restore(state);
                            }
                        }
                    }

                    this.FillCircle(g, Canvas, cavityPx);
                    for (int i = 0; i < n - 1; i++)
                    {
                        this.Circle(g, seam, radii[i]);
                    }

                    // Условный разрыв укороченного кольца — радиальной линией с изломом на 45°.
                    for (int i = 0; i < n; i++)
                    {
                        if (this.shieldPx.SideBroken[i])
                        {
                            float r0 = i == 0 ? cavityPx : radii[i - 1];
                            float mid = 0.5f * (r0 + radii[i]);
                            double a = -Math.PI / 4.0;
                            float cx = this.planCenter.X + (float)(mid * Math.Cos(a)), cy = this.planCenter.Y + (float)(mid * Math.Sin(a));
                            GraphicsState state = g.Save();
                            g.TranslateTransform(cx, cy);
                            g.RotateTransform(-45f);
                            ZigZag(g, seam, false, -12f, 12f, 0f);
                            g.Restore(state);
                        }
                    }

                    this.Circle(g, ink, outerPx);
                    this.Circle(g, ink, cavityPx);
                }

                // 2. Проба и сосуд.
                double holeOuterReach = 0.0;
                double[] xi = new double[4], yi = new double[4], xo = new double[4], yo = new double[4];
                bool trap = m.HasTrapezoidHole && m.TrapezoidHoleVertices(0.0, xi, yi)
                            && m.TrapezoidHoleVertices(Math.Max(m.MarinelliHoleSideThickness, 0.0), xo, yo);
                if (trap)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        holeOuterReach = Math.Max(holeOuterReach, Math.Sqrt(xo[i] * xo[i] + yo[i] * yo[i]));
                    }
                }

                float beakerPx = 0f;
                double boxHalfX = 0.0, boxHalfY = 0.0;
                switch (m.SourceType)
                {
                    case GeometrySourceType.Marinelli:
                    {
                        double rOut = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                        double rh = 0.5 * Math.Max(m.MarinelliHoleDiameter, 0.0);
                        double ths = Math.Max(m.MarinelliHoleSideThickness, 0.0);
                        double rSrc = trap ? Math.Max(holeOuterReach, rOut - Math.Max(m.MarinelliSideThickness, 0.0))
                                           : Math.Max(rh + ths, rOut - Math.Max(m.MarinelliSideThickness, 0.0));
                        beakerPx = (float)(rOut * this.scale);
                        this.FillCircle(g, WallColor, beakerPx);
                        this.FillCircle(g, SampleColor, (float)(rSrc * this.scale));
                        if (trap)
                        {
                            using (Brush wall = new SolidBrush(WallColor))
                            using (Brush canvas = new SolidBrush(Canvas))
                            {
                                g.FillPolygon(wall, this.PlanPoints(xo, yo));
                                g.FillPolygon(canvas, this.PlanPoints(xi, yi));
                            }

                            g.DrawPolygon(ink, this.PlanPoints(xi, yi));
                        }
                        else
                        {
                            this.FillCircle(g, WallColor, (float)((rh + ths) * this.scale));
                            this.FillCircle(g, Canvas, (float)(rh * this.scale));
                            this.Circle(g, ink, (float)(rh * this.scale));
                        }

                        this.Circle(g, ink, beakerPx);
                        this.DrawPlanDetector(g, m, false);
                        break;
                    }

                    case GeometrySourceType.Cylinder:
                    {
                        double rOut = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                        beakerPx = (float)(rOut * this.scale);
                        this.FillCircle(g, WallColor, beakerPx);
                        this.FillCircle(g, SampleColor, (float)(Math.Max(0.0, rOut - m.BeakerSideWallThickness) * this.scale));
                        this.Circle(g, ink, beakerPx);
                        this.DrawPlanDetector(g, m, !this.flipZ);
                        break;
                    }

                    case GeometrySourceType.Box:
                    {
                        double hx = 0.5 * Math.Max(m.BoxSourceX, 0.0), hy = 0.5 * Math.Max(m.BoxSourceY, 0.0);
                        double w = Math.Max(m.BoxSideWallThickness, 0.0);
                        using (Brush wall = new SolidBrush(WallColor))
                        using (Brush sample = new SolidBrush(SampleColor))
                        {
                            g.FillRectangle(wall, this.PX(-hx), this.PY(-hy), (float)(2 * hx * this.scale), (float)(2 * hy * this.scale));
                            g.FillRectangle(sample, this.PX(-hx + w), this.PY(-hy + w), (float)(2 * (hx - w) * this.scale), (float)(2 * (hy - w) * this.scale));
                        }

                        g.DrawRectangle(ink, this.PX(-hx), this.PY(-hy), (float)(2 * hx * this.scale), (float)(2 * hy * this.scale));
                        this.DrawPlanDetector(g, m, !this.flipZ);
                        boxHalfX = hx;
                        boxHalfY = hy;
                        break;
                    }

                    default:
                        // Точка на расстоянии: у сцены в защите прибор стоит НАД точкой и
                        // закрывает её сверху — точка показана штриховым кружком поверх.
                        if (this.flipZ)
                        {
                            this.DrawPlanDetector(g, m, false);
                            using (Pen dash = new Pen(SampleColor, 1.4f) { DashStyle = DashStyle.Dash })
                            {
                                g.DrawEllipse(dash, this.planCenter.X - 5f, this.planCenter.Y - 5f, 10f, 10f);
                            }
                        }
                        else
                        {
                            this.DrawPlanDetector(g, m, true);
                            using (Brush b = new SolidBrush(SampleColor))
                            {
                                g.FillEllipse(b, this.planCenter.X - 4f, this.planCenter.Y - 4f, 8f, 8f);
                            }
                        }

                        break;
                }

                // 3. Размеры: диаметр сосуда — над планом, полость и наружный — под ним, с
                // выносными от касательных; трапеция — внутри, на плашках.
                using (Pen pen = new Pen(Ink, 1f))
                using (Pen leader = new Pen(Ink, 0.8f))
                using (Brush text = new SolidBrush(Ink))
                {
                    float top = this.planCenter.Y - Math.Max(outerPx, beakerPx);
                    float bottom = this.planCenter.Y + Math.Max(outerPx, beakerPx);
                    if (beakerPx > 0f)
                    {
                        float y = top - 12f;
                        g.DrawLine(leader, this.planCenter.X - beakerPx, this.planCenter.Y, this.planCenter.X - beakerPx, y - 3f);
                        g.DrawLine(leader, this.planCenter.X + beakerPx, this.planCenter.Y, this.planCenter.X + beakerPx, y - 3f);
                        string key = m.SourceType == GeometrySourceType.Marinelli ? "MarinelliBeakerDiameter" : "BeakerDiameter";
                        this.DimHAt(g, pen, text, this.planCenter.X - beakerPx, this.planCenter.X + beakerPx, y,
                                    2.0 * beakerPx / this.scale, key);
                    }

                    if (shield)
                    {
                        float y1 = bottom + 14f, y2 = bottom + 32f;
                        g.DrawLine(leader, this.planCenter.X - cavityPx, this.planCenter.Y, this.planCenter.X - cavityPx, y1 + 3f);
                        g.DrawLine(leader, this.planCenter.X + cavityPx, this.planCenter.Y, this.planCenter.X + cavityPx, y1 + 3f);
                        g.DrawLine(leader, this.planCenter.X - outerPx, this.planCenter.Y, this.planCenter.X - outerPx, y2 + 3f);
                        g.DrawLine(leader, this.planCenter.X + outerPx, this.planCenter.Y, this.planCenter.X + outerPx, y2 + 3f);
                        this.DimHAt(g, pen, text, this.planCenter.X - cavityPx, this.planCenter.X + cavityPx, y1,
                                    m.ShieldInnerDiameter, "ShieldInnerDiameter");
                        this.DimHAt(g, pen, text, this.planCenter.X - outerPx, this.planCenter.X + outerPx, y2,
                                    m.ShieldOuterDiameter, null);
                    }

                    if (boxHalfX > 0.0 && boxHalfY > 0.0)
                    {
                        this.plateLabels = true;
                        try
                        {
                            SizeF wy = g.MeasureString(Format(m.BoxSourceY), this.Font);
                            this.DimHAt(g, pen, text, this.PX(-boxHalfX), this.PX(boxHalfX), this.PY(-boxHalfY) - 6f,
                                        m.BoxSourceX, "BoxSourceX");
                            this.DimVAt(g, pen, text, this.PX(-boxHalfX) - 8f - wy.Width, this.PY(-boxHalfY), this.PY(boxHalfY),
                                        m.BoxSourceY, "BoxSourceY");
                        }
                        finally
                        {
                            this.plateLabels = false;
                        }
                    }

                    if (trap)
                    {
                        this.plateLabels = true;
                        try
                        {
                            SizeF b2 = g.MeasureString(Format(m.MarinelliHoleBase2), this.Font);
                            SizeF wd = g.MeasureString(Format(m.MarinelliHoleWidth), this.Font);
                            this.DimHAt(g, pen, text, this.PX(xi[0]), this.PX(xi[1]), this.PY(yo[0]) - 6f,
                                        m.MarinelliHoleBase1, "MarinelliHoleBase1");
                            this.DimHAt(g, pen, text, this.PX(xi[3]), this.PX(xi[2]), this.PY(yo[2]) + 8f + b2.Height,
                                        m.MarinelliHoleBase2, "MarinelliHoleBase2");
                            float xl = this.PX(Math.Min(xo[0], xo[3])) - 8f - wd.Width;
                            this.DimVAt(g, pen, text, xl, this.PY(yi[0]), this.PY(yi[2]), m.MarinelliHoleWidth, "MarinelliHoleWidth");
                        }
                        finally
                        {
                            this.plateLabels = false;
                        }
                    }
                }
            }
        }

        /// <summary>Сечение прибора на плане: обвязка и кристалл; под пробой (не виден сверху) — штриховым контуром.</summary>
        void DrawPlanDetector(Graphics g, GeometryModel m, bool hidden)
        {
            double toSample, lateral;
            GeometryScenes.WrappingInScene(m, out toSample, out lateral);
            double hx, hy;
            bool box = m.Shape == CrystalShape.Box;
            if (box)
            {
                double depth;
                m.CrystalBoxInScene(out hx, out hy, out depth);
            }
            else
            {
                hx = hy = 0.5 * Math.Max(m.CrystalDiameter, 0.0);
            }

            RectangleF outer = RectangleF.FromLTRB(this.PX(-(hx + lateral)), this.PY(-(hy + lateral)),
                                                   this.PX(hx + lateral), this.PY(hy + lateral));
            RectangleF inner = RectangleF.FromLTRB(this.PX(-hx), this.PY(-hy), this.PX(hx), this.PY(hy));
            if (hidden)
            {
                using (Pen dash = new Pen(Ink, 1f) { DashStyle = DashStyle.Dash })
                {
                    if (box)
                    {
                        g.DrawRectangle(dash, outer.X, outer.Y, outer.Width, outer.Height);
                    }
                    else
                    {
                        g.DrawEllipse(dash, outer);
                    }
                }

                return;
            }

            using (Brush clad = new SolidBrush(CladdingColor))
            using (Brush crystal = new SolidBrush(CrystalColor))
            {
                if (box)
                {
                    g.FillRectangle(clad, outer);
                    g.FillRectangle(crystal, inner);
                }
                else
                {
                    g.FillEllipse(clad, outer);
                    g.FillEllipse(crystal, inner);
                }
            }
        }

        PointF[] PlanPoints(double[] xs, double[] ys)
        {
            PointF[] points = new PointF[xs.Length];
            for (int i = 0; i < xs.Length; i++)
            {
                points[i] = new PointF(this.PX(xs[i]), this.PY(ys[i]));
            }

            return points;
        }

        /// <summary>
        /// (`AMBER282`) Высота пробы цилиндра и кюветы: без защиты — справа от тела, как
        /// было; в защите — слева ЗА защитой с выносными от граней пробы (справа, в зазоре
        /// между пробой и полостью, ей не хватало места, а там же стоит высота полости).
        /// </summary>
        void SampleHeightDim(Graphics g, Pen pen, Brush ink, double halfWidth, double z1, double z2,
                             double value, string key, double plainX)
        {
            if (this.shieldPx == null || !(this.model != null && this.model.ShieldInScene))
            {
                this.DimV(g, pen, ink, plainX, z1, z2, value, key);
                return;
            }

            float xl = this.X(-0.5 * Math.Max(this.model.ShieldInnerDiameter, 0.0)) - this.shieldPx.SideTotal - 14f;
            using (Pen leader = new Pen(Ink, 0.8f))
            {
                g.DrawLine(leader, this.X(-halfWidth), this.Y(z1), xl - 3f, this.Y(z1));
                g.DrawLine(leader, this.X(-halfWidth), this.Y(z2), xl - 3f, this.Y(z2));
            }

            this.DimVAt(g, pen, ink, xl, this.Y(z1), this.Y(z2), value, key);
        }

        /// <summary>
        /// Есть ли у пробы сосуд. Сосуд — это стенки; проба без единой стенки
        /// лежит навалом (грунт под прибором, лунка в грунте), и «высота
        /// сосуда» у неё не размер, а незаполненное поле модели.
        /// </summary>
        /// <remarks>
        /// ⛔ `E39`. Признак нарочно взят по СТЕНКАМ, а не по равенству высоты
        /// сосуда высоте пробы. Равенство — совпадение чисел, и решать за
        /// человека, что оно неслучайно, чертёж не вправе: ввёл равные руками —
        /// оба размера обязаны стоять на чертеже. Отсутствие обеих стенок —
        /// суждение о ПРЕДМЕТЕ, а не о числах, и его чертёж выносить вправе.
        ///
        /// ⚠ В дереве этому признаку отвечают ровно две сцены —
        /// <c>GeometryScenes.Ground</c> и <c>GeometryScenes.Borehole</c>, и
        /// ровно они же ставят «сосуд = проба». Других сцен, обнуляющих обе
        /// стенки, нет (проверено счётом присвоений в `GeometryScenes.cs`).
        /// </remarks>
        static bool VesselExists(double sideWall, double endWall)
        {
            return sideWall > 0.0 || endWall > 0.0;
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
                // Высота — разностью Y, а не L: у разрыва полости защиты (`AMBER282`)
                // ось z не линейна.
                float y1 = this.Y(z), y2 = this.Y(z + height);
                g.FillRectangle(brush, this.X(x), Math.Min(y1, y2), this.L(width), Math.Abs(y2 - y1));
            }
        }

        void Outline(Graphics g, Pen pen, double x, double z, double width, double height)
        {
            if (!(width > 0.0) || !(height > 0.0))
            {
                return;
            }

            float y1 = this.Y(z), y2 = this.Y(z + height);
            g.DrawRectangle(pen, this.X(x), Math.Min(y1, y2), this.L(width), Math.Abs(y2 - y1));
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

            this.DimHAt(g, pen, ink, this.X(x1), this.X(x2), this.Y(z), value, key);
        }

        /// <summary>(`AMBER282`) Горизонтальный размер по ТОЧКАМ — для размеров у полос защиты, которые стоят в точках.</summary>
        void DimHAt(Graphics g, Pen pen, Brush ink, float a, float b, float y, double value, string key)
        {
            if (!(value > 0.0))
            {
                return;
            }

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

            this.DimVAt(g, pen, ink, this.X(x), this.Y(z1), this.Y(z2), value, key);
        }

        /// <summary>(`AMBER282`) Вертикальный размер по ТОЧКАМ.</summary>
        void DimVAt(Graphics g, Pen pen, Brush ink, float xx, float a, float b, double value, string key)
        {
            if (!(value > 0.0))
            {
                return;
            }

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
            if (this.plateLabels)
            {
                // (`AMBER282`) Число вида сверху на тёмной пробе — на светлой плашке.
                using (Brush plate = new SolidBrush(Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF)))
                {
                    g.FillRectangle(plate, x - 1f, y, size.Width + 2f, size.Height);
                }
            }

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
            string text = "Y = " + value.ToString("G4", CultureInfo.InvariantCulture)
                          + " " + Resources.GeometryEditorUnitMm;
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
            bool free = false;
            for (int i = 0; i < spots.Count; i++)
            {
                float cost = this.Cost(spots[i].Box);
                if (cost <= 0f)
                {
                    best = i;
                    free = true;
                    break;
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = i;
                }
            }

            // ⛔ `E40`. ЗДЕСЬ И ТОЛЬКО ЗДЕСЬ становится известно, что месту не
            // нашлось замены. Ниже число уже напечатано и от свободного на вид
            // неотличимо — ни `Labels`, ни картинка прижима не показывают, пока
            // подписи фактически не пересеклись (зазор `Gap` в две точки
            // пересечением не считается, а читается как одно длинное число).
            if (!free)
            {
                this.crowded++;
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

            // (`AMBER282`) Полосы защиты — препятствие тем же весом, что чужая подпись:
            // число на штриховке не читается.
            foreach (RectangleF band in this.obstacles)
            {
                RectangleF hit = RectangleF.Intersect(grown, band);
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
