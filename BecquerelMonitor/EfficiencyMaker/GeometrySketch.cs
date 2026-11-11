using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
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

        // ------------------------------------------------------------------
        // (`AMBER325`) Сторона второго вида на вкладке «Источник». Amber 11.11.2026, к снимку вкладки «Источник»,
        // дословно: «Запиши задачу, нужно переделать скетч на From detector side.»; решения вопросником того же дня,
        // дословно: «Сделай в скетче CheckBox - с какой стороны показывать образец. С верху или с низу. Пускай
        // пользователь решает.»; умолчание — «Со стороны детектора, помнить в настройках (Рекомендую)»; где — «Только
        // «Источник» (Рекомендую)». Галка живёт в самом чертеже, у заголовка второго вида; выбор — одна настройка
        // приложения (`GlobalConfigInfo.GeometrySketchPlanFromSample`), кривая и матрица от неё не зависят.
        // ------------------------------------------------------------------

        bool? planFromSample;
        CheckBox planSideCheck;
        bool syncingPlanSide;

        /// <summary>Показана ли галка: свой признак, а не `Visible` — у того на скрытом родителе всегда «нет».</summary>
        bool planCheckShown;

        /// <summary>(`AMBER325`) Галка без подписи — один квадрат, подпись во всплывающей подсказке (узкий план).</summary>
        bool planCheckBare;

        /// <summary>(`AMBER325`, третий заход) Галка с короткой подписью «от пробы» / «from sample».</summary>
        bool planCheckShort;

        ToolTip planSideTip;

        /// <summary>Где галка встала в последней отрисовке; пусто — галки нет (не «Источник» или нет второго вида). Читает проба.</summary>
        Rectangle planCheckRect;

        /// <summary>
        /// (`AMBER325`) Второй вид «Источника» со стороны пробы (иначе — со стороны детектора, умолчание). Читается из
        /// главного конфига при первой надобности; присваивание перерисовывает, но в конфиг не пишет — пишет только галка.
        /// </summary>
        public bool PlanFromSample
        {
            get
            {
                if (this.planFromSample == null)
                {
                    this.planFromSample = PlanSideStore.Read();
                }

                return this.planFromSample.Value;
            }
            set
            {
                if (this.planFromSample != value)
                {
                    this.planFromSample = value;
                    this.Invalidate();
                }
            }
        }

        /// <summary>(`AMBER325`) Рамка галки последней отрисовки; пусто — галки нет. Читает проба.</summary>
        public Rectangle PlanSideCheckBounds
        {
            get { return this.planCheckRect; }
        }

        /// <summary>(`AMBER325`) Второй вид «Источника» — со стороны детектора: ось Y плана вверх, прибор поверх пробы.</summary>
        bool PlanFromDetector
        {
            get { return this.Mode == SketchMode.Source && !this.PlanFromSample; }
        }

        /// <summary>
        /// (`AMBER325`) Хранилище выбора — главный конфиг программы, как галка «Use Nvidia GPU» (`AMBER219`): запись сразу при
        /// переключении. Без конфига (безоконная проба без `config\BecquerelMonitor.xml`) — умолчание, со стороны детектора, и
        /// запись не делается: встроенные умолчания конфига на диск не пишутся.
        /// </summary>
        public static class PlanSideStore
        {
            public static bool Read()
            {
                try
                {
                    GlobalConfigInfo config = GlobalConfigManager.GetInstance().GlobalConfig;
                    return config != null && config.GeometrySketchPlanFromSample;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            public static void Write(bool fromSample)
            {
                GlobalConfigManager manager;
                try
                {
                    manager = GlobalConfigManager.GetInstance();
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                if (manager.GlobalConfig != null && manager.GlobalConfig.GeometrySketchPlanFromSample != fromSample)
                {
                    manager.GlobalConfig.GeometrySketchPlanFromSample = fromSample;
                    manager.SaveConfigFile();
                }
            }
        }

        /// <summary>
        /// (`AMBER325`) Поставить галку туда, где её место отвела отрисовка, или убрать. Меняется только то, что изменилось:
        /// скрытие и показ дочернего окна заказывают перерисовку родителя, и безусловная смена дала бы бесконечный круг.
        /// </summary>
        void PlacePlanSideCheck()
        {
            if (this.planCheckRect.IsEmpty)
            {
                if (this.planSideCheck != null && this.planCheckShown)
                {
                    this.planCheckShown = false;
                    this.planSideCheck.Visible = false;
                }

                return;
            }

            if (this.planSideCheck == null)
            {
                // (`AMBER325`, четвёртый заход) Фон прозрачный: рамка галки по `PreferredSize` выше строки заголовка, и её поля
                // не должны закрывать соседнюю строку.
                this.planSideCheck = new CheckBox
                {
                    AutoSize = false,
                    BackColor = Color.Transparent,
                    ForeColor = Ink,
                    TabStop = true,
                    UseVisualStyleBackColor = false,
                };
                this.planSideCheck.CheckedChanged += this.PlanSideCheckChanged;
                this.Controls.Add(this.planSideCheck);
            }

            if (this.planSideCheck.Font != this.Font)
            {
                this.planSideCheck.Font = this.Font;
            }

            // Узкий план — короткая подпись или один квадрат; полная подпись — в подсказке и в имени для чтеца экрана, всегда.
            string text = Resources.EfficiencySketchFromSampleCheck;
            string shown = this.planCheckBare ? "" : this.planCheckShort ? Resources.EfficiencySketchFromSampleShort : text;
            if (this.planSideCheck.Text != shown)
            {
                this.planSideCheck.Text = shown;
            }

            if (this.planSideCheck.AccessibleName != text)
            {
                this.planSideCheck.AccessibleName = text;
                if (this.planSideTip == null)
                {
                    this.planSideTip = new ToolTip();
                }

                this.planSideTip.SetToolTip(this.planSideCheck, text);
            }

            if (this.planSideCheck.Checked != this.PlanFromSample)
            {
                this.syncingPlanSide = true;
                try
                {
                    this.planSideCheck.Checked = this.PlanFromSample;
                }
                finally
                {
                    this.syncingPlanSide = false;
                }
            }

            if (this.planSideCheck.Bounds != this.planCheckRect)
            {
                this.planSideCheck.Bounds = this.planCheckRect;
            }

            if (!this.planCheckShown)
            {
                this.planCheckShown = true;
                this.planSideCheck.Visible = true;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && this.planSideTip != null)
            {
                this.planSideTip.Dispose();
                this.planSideTip = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>(`AMBER325`) Галка без подписи (узкий план); читает проба.</summary>
        public bool PlanSideCheckBare
        {
            get { return this.planCheckBare; }
        }

        /// <summary>(`AMBER325`, четвёртый заход) `PreferredSize` галки с этой подписью и шрифтом чертежа (рамка не меньше).</summary>
        Size CheckPreferredSize(string shown)
        {
            using (CheckBox measure = new CheckBox { AutoSize = false, Text = shown, Font = this.Font })
            {
                return measure.PreferredSize;
            }
        }

        /// <summary>(`AMBER325`, третий заход) Галка с короткой подписью (узкий план); читает проба.</summary>
        public bool PlanSideCheckShort
        {
            get { return this.planCheckShort; }
        }

        void PlanSideCheckChanged(object sender, EventArgs e)
        {
            if (this.syncingPlanSide)
            {
                return;
            }

            bool fromSample = this.planSideCheck.Checked;
            this.PlanFromSample = fromSample;
            PlanSideStore.Write(fromSample);
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
        protected override void OnPaint(PaintEventArgs e)
        {
            // (`AMBER325`) Место галки стороны второго вида отводит отрисовка заголовков; ставится галка после неё, один раз.
            this.planCheckRect = Rectangle.Empty;
            this.planCheckBare = false;
            this.planCheckShort = false;
            this.PaintSketch(e);
            this.PlacePlanSideCheck();
        }

        void PaintSketch(PaintEventArgs e)
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

            this.sidePanel = this.planPanel = Rectangle.Empty;
            this.planView = false;
            this.dimLines.Clear();
            this.dimHidden.Clear();
            this.dimEnds.Clear();
            this.dimNumbers.Clear();
            this.dimGroups.Clear();
            this.sideContour = this.planContour = RectangleF.Empty;
            this.costField = RectangleF.Empty;
            GeometryModel m = this.model;
            if (m == null)
            {
                this.DrawFieldFrame(g);
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
                this.DrawFieldFrame(g);
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

            // Поля считает простановка размеров (`AMBER307`, ГОСТ 2.307-2011): сколько рядов на стороне, столько
            // и поля. Миниатюре поля не нужны: размеров на ней нет, а место дорого.
            bool overview = this.Mode == SketchMode.Overview;
            string volumeText = this.VolumeText(m);
            SizeF volumeSize = SizeF.Empty;
            float plateRoom = 0f;
            if (volumeText != null)
            {
                volumeSize = g.MeasureString(volumeText, this.Font);
                plateRoom = VolumePlateBottom(volumeSize);
            }

            double worldWidth = Math.Max(right - left, 1e-6);
            double worldHeight = Math.Max(bottom - top, 1e-6);

            // (`AMBER307`, Amber 06.11.2026 вопросником, дословно: «Рисовать на всех эскизах вид сверху, со
            // стороны детектора. Кроме импорта GDML - там уже это есть.» и «Да, сделай по аналогии, как в
            // импорте gdml (рядом отдельный скетч вид сверху)».) На вкладках редактора — ДВА отдельных вида одним
            // масштабом: разрез и план, каждый в своей рамке со своим заголовком, как в окне импорта GDML; рядом
            // или друг под другом — как выходит крупнее. На разрезе положение бруска не видно, на плане видно.
            // Миниатюра окна прибора — один вид: её задача дать узнать конфигурацию.
            // Вкладка «Защита» — тоже один вид. Amber 06.11.2026 вопросником, дословно: «Не нужно для закладки
            // "Защита" делать вид сверху. Только для "Детектор" и "Образец".» Её чертёж отличает `ShowShield`.
            // (`AMBER282`) Легенда защиты — полосой снизу во всю ширину.
            bool wantPlan = this.Mode == SketchMode.Detector || (this.Mode == SketchMode.Source && !this.ShowShield);
            double planRx = 0.0, planRy = 0.0;
            if (wantPlan)
            {
                this.PlanExtent(m, zFace, out planRx, out planRy);
                wantPlan = planRx > 0.0 && planRy > 0.0;
            }

            this.LayoutSideBlock(g, m, hasShield);
            float reserveY = this.legendRect.IsEmpty ? 0f : this.legendRect.Height + 8f;
            int areaHeight = this.Height - (int)Math.Ceiling(reserveY);
            float headSide = 0f, headPlan = 0f;

            // Запасной вид без плана: подпись разворота и глубина всё равно печатаются, над табличкой.
            float facingRoom = 0f;
            if (!overview)
            {
                foreach (HeaderLine line in this.FacingLines(g, m, this.Width))
                {
                    facingRoom += g.MeasureString(line.Text, this.Font).Height;
                }

                if (facingRoom > 0f)
                {
                    facingRoom += 6f;
                }
            }

            this.worldLeft = left;
            this.worldTop = top;
            // (`AMBER282`) Сцена в защите — дном вниз (`flipZ`): сверху полосы крышки.
            // Так же и на вкладке «Источник», где защита не рисуется: та же проба в той же
            // установке не должна на соседней вкладке висеть вверх ногами.
            // И в миниатюре окна прибора (Amber 03.11.2026: «А ты не забыл перевернуть вот
            // это?») — один вид, но той же ориентации, что вид сбоку редактора.
            this.flipZ = (this.Mode == SketchMode.Source || overview) && m.ShieldInScene;
            this.dimOuterHalf = outerHalf;

            // Поля разреза и плана, сменяются на каждом круге: слева, справа, сверху, снизу.
            float[] sideRoom = overview ? new[] { 10f, 10f, 8f, 8f } : new[] { 30f, 30f, 30f, 30f };
            float[] planRoom = { 30f, 30f, 30f, 30f };
            bool plan = wantPlan;
            float floorPx = 0f, lidPx = 0f, sidePx = 0f;

            // Вторичные размеры скрываются по приоритету, когда тело стало мельче порога: сперва все,
            // потом без толщин, потом только главные. Поле в фокусе показывает свой размер всё равно.
            // (`AMBER324`) Ряды сбоку сперва разнесены по ширине числа (решение Amber 09.11.2026 «Разнести ряды»). Если и
            // на одних главных размерах тело мельче порога (поле 198×367 со сценой в защите: тело сжималось в точку), второй
            // проход ставит ряды сбоку прежним шагом `DimPitchPx`: число внутреннего ряда, и главного тоже, уходит на полку,
            // но не скрывается, и чертёж остаётся чертежом. Решения Amber 09.11.2026 вопросником, дословно: о проходе —
            // «Оставить (Рекомендую)»; о маринелли в защите в окнах шириной 280 — «Крупнее, «110» на полке (Рекомендую)».
            this.compactRows = false;
            for (int rowsPass = 0; rowsPass < 2; rowsPass++)
            {
                for (this.dimLevel = overview ? 0 : 3; this.dimLevel >= 0; this.dimLevel--)
                {
                    bool retried = false;
                    for (int round = 0; round < 5; round++)
                    {
                        float mL = sideRoom[0], mR = sideRoom[1], mT = sideRoom[2] + plateRoom, mB = sideRoom[3];
                        float pL = planRoom[0], pR = planRoom[1], pT = planRoom[2], pB = planRoom[3];

                        // Масштаб — по ПОЛОСТИ и по рисуемым полосам слоёв: полосы в точках зависят
                        // от масштаба (тонкий слой — не меньше 3 точек, толстый — не больше предела),
                        // поэтому три прохода; каждый следующий масштаб не меньше предыдущего.
                        plan = wantPlan;
                        double fit = 0.0;
                        for (int attempt = 0; attempt < 2; attempt++)
                        {
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
                                double sx = (this.Width - mL - mR - 2.0 * side) / worldWidth;
                                double sy = (this.Height - mT - (plan ? 0f : facingRoom) - mB - reserveY - vert - this.breakPx) / sideH;
                                bool vertical = sy < sx;
                                double found;
                                if (!plan)
                                {
                                    found = Math.Min(sx, sy);
                                }
                                else
                                {
                                    double dx = 2.0 * planRx, dy = 2.0 * planRy;

                                    // Друг под другом.
                                    double stackedHead = this.HeaderHeight(g, m, false, this.Width) + this.HeaderHeight(g, m, true, this.Width);
                                    double sx1 = Math.Min(sx, (this.Width - pL - pR - 2.0 * side) / dx);
                                    double sy1 = (areaHeight - ViewGap - stackedHead - mT - mB - pT - pB
                                                  - vert - 2.0 * side - this.breakPx) / (sideH + dy);

                                    // Рядом.
                                    float half = 0.5f * this.Width;
                                    double sx2 = (this.Width - ViewGap - mL - mR - pL - pR - 4.0 * side) / (worldWidth + dx);
                                    double sy2 = Math.Min((areaHeight - this.HeaderHeight(g, m, false, half) - mT - mB - vert - this.breakPx) / sideH,
                                                          (areaHeight - this.HeaderHeight(g, m, true, half) - pT - pB - 2.0 * side) / dy);
                                    double s1 = Math.Min(sx1, sy1), s2 = this.Width >= SideBySideMinWidth ? Math.Min(sx2, sy2) : double.NegativeInfinity;
                                    this.planStacked = s1 >= s2;
                                    found = Math.Max(s1, s2);
                                    vertical = this.planStacked ? sy1 < sx1 : sy2 < sx2;
                                }

                                // Полосы защиты на первом проходе — наибольшие. Если с ними места нет, это ещё не
                                // отказ: следующий проход берёт полосы мелкого масштаба, они много тоньше.
                                fit = found;
                                this.scale = found > 0.0 ? found : 1e-3;
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

                            // Двум видам места нет: один разрез, как было. Лучше один читаемый, чем ни одного.
                            if (!plan)
                            {
                                this.scale = fit > 0.0 ? fit : 0.0;
                                break;
                            }

                            if (fit * 2.0 * Math.Max(planRx, planRy) >= MinPlanPx)
                            {
                                this.scale = fit;
                                break;
                            }

                            plan = false;
                        }

                        if (hasShield)
                        {
                            this.shieldPx = this.BandsPx(m, this.scale);
                        }

                        if (!(this.scale > 0.0) || double.IsInfinity(this.scale))
                        {
                            // Поля прошлого уровня здесь не вмещаются: круг заново от скромных, раз.
                            if (retried || overview)
                            {
                                break;
                            }

                            retried = true;
                            sideRoom = new[] { 30f, 30f, 30f, 30f };
                            planRoom = new[] { 30f, 30f, 30f, 30f };
                            continue;
                        }

                        floorPx = this.shieldPx != null ? (this.flipZ ? this.shieldPx.LidTotal : this.shieldPx.FloorTotal) : 0f;
                        lidPx = this.shieldPx != null ? (this.flipZ ? this.shieldPx.FloorTotal : this.shieldPx.LidTotal) : 0f;
                        sidePx = this.shieldPx != null ? this.shieldPx.SideTotal : 0f;
                        double drawnHeight = (worldHeight - (this.breakZ1 - this.breakZ0)) * this.scale + this.breakPx;
                        double drawnWidth = worldWidth * this.scale;
                        this.drawnPx = drawnHeight;
                        this.planView = plan;
                        this.planRadiusX = planRx;
                        this.planRadiusY = planRy;
                        double planW = 2.0 * planRx * this.scale, planH = 2.0 * planRy * this.scale;
                        if (!plan)
                        {
                            this.sidePanel = this.planPanel = Rectangle.Empty;
                            this.padLeft = (int)(mL + sidePx + Math.Max(0.0, this.Width - mL - mR - 2.0 * sidePx - drawnWidth) / 2.0);
                            this.padTop = (int)(facingRoom + mT + floorPx + Math.Max(0.0, this.Height - facingRoom - mT - mB - reserveY - floorPx - lidPx - drawnHeight) / 2.0);
                        }
                        else if (this.planStacked)
                        {
                            headSide = this.HeaderHeight(g, m, false, this.Width);
                            headPlan = this.HeaderHeight(g, m, true, this.Width);
                            double usedSide = headSide + mT + floorPx + drawnHeight + lidPx + mB;
                            double usedPlan = headPlan + pT + planH + 2.0 * sidePx + pB;
                            double free = Math.Max(0.0, areaHeight - ViewGap - usedSide - usedPlan);
                            int sideHeight = (int)(usedSide + free * usedSide / (usedSide + usedPlan));
                            this.sidePanel = new Rectangle(0, 0, this.Width, sideHeight);
                            this.planPanel = new Rectangle(0, sideHeight + ViewGap, this.Width, areaHeight - sideHeight - ViewGap);
                            this.padLeft = (int)(mL + sidePx + Math.Max(0.0, this.Width - mL - mR - 2.0 * sidePx - drawnWidth) / 2.0);
                            this.padTop = (int)headSide + (int)mT + (int)floorPx + (int)Math.Max(0.0, (sideHeight - usedSide) / 2.0);
                            this.planCenter = new PointF((float)(pL + sidePx + planW / 2.0 + Math.Max(0.0, this.Width - pL - pR - 2.0 * sidePx - planW) / 2.0),
                                                         (float)(this.planPanel.Top + headPlan + pT + sidePx + planH / 2.0
                                                                 + Math.Max(0.0, (this.planPanel.Height - usedPlan) / 2.0)));
                        }
                        else
                        {
                            double usedSide = mL + mR + 2.0 * sidePx + drawnWidth;
                            double usedPlan = pL + pR + 2.0 * sidePx + planW;
                            double free = Math.Max(0.0, this.Width - ViewGap - usedSide - usedPlan);
                            int sideWidth = (int)(usedSide + free * usedSide / (usedSide + usedPlan));
                            this.sidePanel = new Rectangle(0, 0, sideWidth, areaHeight);
                            this.planPanel = new Rectangle(sideWidth + ViewGap, 0, this.Width - sideWidth - ViewGap, areaHeight);
                            headSide = this.HeaderHeight(g, m, false, this.sidePanel.Width);
                            headPlan = this.HeaderHeight(g, m, true, this.planPanel.Width);
                            this.padLeft = (int)(mL + sidePx + Math.Max(0.0, sideWidth - usedSide) / 2.0);
                            double usedTall = headSide + mT + floorPx + drawnHeight + lidPx + mB;
                            this.padTop = (int)headSide + (int)mT + (int)floorPx + (int)Math.Max(0.0, (areaHeight - usedTall) / 2.0);
                            double usedPlanTall = headPlan + pT + planH + 2.0 * sidePx + pB;
                            this.planCenter = new PointF((float)(this.planPanel.Left + pL + sidePx + planW / 2.0 + Math.Max(0.0, this.planPanel.Width - usedPlan) / 2.0),
                                                         (float)(headPlan + pT + sidePx + planH / 2.0 + Math.Max(0.0, (areaHeight - usedPlanTall) / 2.0)));
                        }

                        if (overview)
                        {
                            break;
                        }

                        // Круг замыкается: теперь известно, какие размеры стоят и какие из них тонки при этом масштабе.
                        this.sideContour = RectangleF.FromLTRB(this.padLeft - sidePx, this.padTop - floorPx,
                                                               (float)(this.padLeft + drawnWidth + sidePx), (float)(this.padTop + drawnHeight + lidPx));
                        this.planContour = !plan ? RectangleF.Empty
                            : RectangleF.FromLTRB((float)(this.planCenter.X - planW / 2.0 - sidePx), (float)(this.planCenter.Y - planH / 2.0 - sidePx),
                                                  (float)(this.planCenter.X + planW / 2.0 + sidePx), (float)(this.planCenter.Y + planH / 2.0 + sidePx));
                        this.QueueDimensions(g, m, halfWidth, height, tfr, tsr, tfc, tsc, tm, tfg, tsg, zFace);
                        float[] newSide = this.DimensionRoom(g, false);
                        float[] newPlan = this.DimensionRoom(g, true);
                        if (m.Scene == GeometrySceneKind.Iso && this.Mode == SketchMode.Source)
                        {
                            newSide[2] = Math.Max(newSide[2], 20f);
                        }

                        bool settled = true;
                        for (int i = 0; i < 4; i++)
                        {
                            settled &= Math.Abs(newSide[i] - sideRoom[i]) < 1f && Math.Abs(newPlan[i] - planRoom[i]) < 1f;
                            sideRoom[i] = newSide[i];
                            planRoom[i] = newPlan[i];
                        }

                        if (settled)
                        {
                            break;
                        }
                    }

                    if (overview || this.dimLevel == 0)
                    {
                        break;
                    }

                    if (this.scale > 0.0 && plan == wantPlan && (this.dimLevel == 1 || Math.Max(worldWidth, worldHeight) * this.scale >= (hasShield ? MinBodyInShieldPx : MinBodyPx)))
                    {
                        break;
                    }
                }

                bool bodyOk = this.scale > 0.0 && Math.Max(worldWidth, worldHeight) * this.scale >= (hasShield ? MinBodyInShieldPx : MinBodyPx);
                if (overview || bodyOk || this.compactRows)
                {
                    break;
                }

                this.compactRows = true;
            }

            if (!(this.scale > 0.0) || double.IsInfinity(this.scale))
            {
                this.sidePanel = this.planPanel = Rectangle.Empty;
                this.planView = false;
                this.dimGroups.Clear();
                this.DrawFieldFrame(g);
                return;
            }

            if (plan)
            {
                using (Pen frame = new Pen(Color.FromArgb(0x90, 0x90, 0x90)))
                {
                    g.DrawRectangle(frame, this.sidePanel.X, this.sidePanel.Y, this.sidePanel.Width - 1, this.sidePanel.Height - 1);
                    g.DrawRectangle(frame, this.planPanel.X, this.planPanel.Y, this.planPanel.Width - 1, this.planPanel.Height - 1);
                }
            }
            else
            {
                this.DrawFieldFrame(g);
            }

            this.plateX = plan ? this.sidePanel.Left : 0f;
            this.plateY = plan ? headSide : facingRoom;

            if (this.Mode != SketchMode.Detector)
            {
                this.DrawSource(g, m, zFace);
            }

            this.DrawDetector(g, m, halfWidth, height, tfr, tsr, tfc, tsc, tm, tfg, tsg);

            this.DrawPlanView(g, m);

            // Табличка и заголовки — ДО размеров: их прямоугольники уже лежат в `labels`,
            // и раскладка чисел их обходит, как любую подпись.
            if (volumeText != null)
            {
                this.DrawVolume(g, volumeText, volumeSize);
            }

            this.DrawViewTitles(g, m);

            if (overview)
            {
                this.AnnotateOverview(g, m);
            }
            else
            {
                if (this.Mode == SketchMode.Source)
                {
                    this.DrawShieldLegend(g, m);
                }

                this.QueueDimensions(g, m, halfWidth, height, tfr, tsr, tfc, tsc, tm, tfg, tsg, zFace);
                this.FlushDimensions(g, false);
                this.DrawFieldRadius(g, m, zFace);
                this.NotesWithoutPlan(g, m, height, tm, boxDepthIntoPage, zFace);
                if (this.planView)
                {
                    this.FlushDimensions(g, true);
                }
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
                // В узком поле длинное число прижимается к краю, а не уходит за него.
                float field = this.planView ? this.sidePanel.Width : this.Width;
                float indent = Math.Max(0f, Math.Min(8f, field - size.Width - 1f));
                g.FillRectangle(plate, this.plateX + indent - 4f, this.plateY + 4f, size.Width + 8f, VolumePlateBottom(size) - 4f);
                this.Print(g, ink, text, null, this.plateX + indent, this.plateY + 6f, size);
            }
        }

        /// <summary>Рамка всего поля: у миниатюры и когда видов нет.</summary>
        void DrawFieldFrame(Graphics g)
        {
            using (Pen frame = new Pen(Color.FromArgb(0x90, 0x90, 0x90)))
            {
                g.DrawRectangle(frame, 0, 0, this.Width - 1, this.Height - 1);
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
                      double tfr, double tsr, double tfc, double tsc, double tm,
                      double tfg, double tsg)
        {
            double outerHalf = halfWidth + tsr + tsg + tsc;
            double zFace = -(tfr + tfg + tfc);
            double gapHalf = halfWidth + tsr + tsg, reflHalf = halfWidth + tsr;

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

            // Поперечник — над телом; длина с оправой — справа цепочкой; обвязка у торца — слева цепочкой,
            // обвязка сбоку — снизу цепочкой. Нулевой слой размера не получает (`AMBER1`).
            this.DimX(g, DimSide.Top, 0, -halfWidth, halfWidth, 0.0, 0.0, 2.0 * halfWidth, widthKey, RankMain);

            this.DimZ(g, DimSide.Right, 1, 0.0, height, halfWidth, halfWidth, height, lengthKey, RankMain);
            this.DimZ(g, DimSide.Right, 1, height, height + tm, halfWidth, outerHalf, tm, "MountingThickness", RankSecond);

            this.DimZ(g, DimSide.Left, 1, zFace, -(tfr + tfg), -outerHalf, -gapHalf, tfc, kfc, RankSecond);
            this.DimZ(g, DimSide.Left, 1, -(tfr + tfg), -tfr, -gapHalf, -reflHalf, tfg, kfg, RankThin);
            this.DimZ(g, DimSide.Left, 1, -tfr, 0.0, -reflHalf, -halfWidth, tfr, kfr, RankThin);

            // Обвязка сбоку — на уровне кристалла, числа слева: короткой выноской в свободное поле.
            float level = this.Y(0.3 * height);
            this.DimThrough(g, DimSide.Left, 1, this.X(-outerHalf), this.X(-gapHalf), level, tsc, ksc, RankSecond);
            this.DimThrough(g, DimSide.Left, 1, this.X(-gapHalf), this.X(-reflHalf), level, tsg, ksg, RankThin);
            this.DimThrough(g, DimSide.Left, 1, this.X(-reflHalf), this.X(-halfWidth), level, tsr, ksr, RankThin);
        }

        /// <summary>
        /// Боковая стенка сосуда: на уровне пробы, числом влево. В защите слева полость и слои, короткого пути там нет:
        /// стенка остаётся рядом у отверстия с выносными.
        /// </summary>
        void WallAtEdge(Graphics g, DimSide near, double x1, double x2, double level, double zEdge, double value, string key)
        {
            if (this.shieldPx == null)
            {
                this.DimThrough(g, DimSide.Left, 2, this.X(x1), this.X(x2), this.Y(level), value, key, RankThin);
            }
            else
            {
                this.DimX(g, near, 0, x1, x2, zEdge, zEdge, value, key, RankThin);
            }
        }

        /// <summary>Глубину бруска или кюветы при одном разрезе называет надпись: плана, где она стояла бы размером, нет.</summary>
        void NotesWithoutPlan(Graphics g, GeometryModel m, double height, double tm, double boxDepthIntoPage, double zFace)
        {
            if (this.planView)
            {
                return;
            }

            using (Brush ink = new SolidBrush(Ink))
            {
                if (this.Mode == SketchMode.Detector && m.Shape == CrystalShape.Box)
                {
                    double hx, hy, d;
                    string kx, pageKey, kd;
                    m.CrystalBoxInScene(out hx, out hy, out d, out kx, out pageKey, out kd);
                    this.Note(g, ink, boxDepthIntoPage, 0.0, height + tm, 18f, pageKey);
                }
                else if (this.Mode == SketchMode.Source && m.Scene != GeometrySceneKind.Iso && m.SourceType == GeometrySourceType.Box)
                {
                    double half = 0.5 * Math.Max(m.BoxSourceX, 0.0);
                    double zWall = zFace - Math.Max(m.BoxToDetectorDistance, 0.0);
                    double zSrc = zWall - Math.Max(m.BoxEndWallThickness, 0.0);
                    this.Note(g, ink, m.BoxSourceY, -half, zSrc - 0.5 * Math.Max(m.BoxSourceHeight, 0.0), -6f, "BoxSourceY", -8f);
                }
            }
        }

        /// <summary>Сцена поля: радиус подписан над окружностью словами, линия по окружности не в масштабе лгала бы.</summary>
        void DrawFieldRadius(Graphics g, GeometryModel m, double zFace)
        {
            if (this.Mode != SketchMode.Source || m.Scene != GeometrySceneKind.Iso)
            {
                return;
            }

            double zc, rr;
            FieldRing(m, zFace, out zc, out rr);
            string text = "R = " + Format(Math.Max(m.FieldRadius, 0.0));
            SizeF size = g.MeasureString(text, this.Font);
            bool lit = this.Lit("FieldRadius");
            using (Brush brush = new SolidBrush(lit ? LitColor : Ink))
            {
                // Через Print, а не голым DrawString (П329): подпись мимо него в `Labels` не попадала.
                this.Print(g, brush, text, "FieldRadius", this.X(0.0) - size.Width / 2f,
                           Math.Min(this.Y(zc - rr), this.Y(zc + rr)) - size.Height - 2f, size);
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
            // Поле: единственный размер — радиус, он подписан словами (`DrawFieldRadius`).
            if (m.Scene == GeometrySceneKind.Iso)
            {
                return;
            }

            // Сторона прибора и сторона пробы: при сцене в защите чертёж перевёрнут (`flipZ`).
            DimSide far = this.flipZ ? DimSide.Bottom : DimSide.Top;
            DimSide near = this.flipZ ? DimSide.Top : DimSide.Bottom;
            double outerHalf = this.dimOuterHalf;
            switch (m.SourceType)
            {
                case GeometrySourceType.Point:
                    this.DimZ(g, DimSide.Right, 0, zFace - Math.Max(m.PointDistance, 0.0), zFace, 0.0, outerHalf,
                              Math.Max(m.PointDistance, 0.0), "PointDistance", RankMain);
                    return;

                case GeometrySourceType.Box:
                {
                    double half = 0.5 * Math.Max(m.BoxSourceX, 0.0);
                    double endB = Math.Max(m.BoxEndWallThickness, 0.0);
                    double hsB = Math.Max(m.BoxSourceHeight, 0.0);
                    double zWall = zFace - Math.Max(m.BoxToDetectorDistance, 0.0);
                    double zSrc = zWall - endB;
                    double wallB = Math.Max(m.BoxSideWallThickness, 0.0);
                    this.DimX(g, far, 0, -half, half, zSrc - hsB, zSrc - hsB, 2.0 * half, "BoxSourceX", RankMain);
                    this.WallAtEdge(g, near, -half, -(half - wallB), zSrc - 0.5 * hsB, zWall, wallB, "BoxSideWallThickness");

                    // Проба, стенка и расстояние идут подряд: цепочка на одной линии.
                    this.DimZ(g, DimSide.Right, 1, zSrc - hsB, zSrc, half, half, hsB, "BoxSourceHeight", RankMain);
                    this.DimZ(g, DimSide.Right, 1, zSrc, zWall, half, half, endB, "BoxEndWallThickness", RankThin);
                    this.DimZ(g, DimSide.Right, 1, zWall, zFace, half, outerHalf, Math.Max(m.BoxToDetectorDistance, 0.0), "BoxToDetectorDistance", RankSecond);
                    return;
                }

                case GeometrySourceType.Cylinder:
                {
                    double rOut = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                    double wall = Math.Max(m.BeakerSideWallThickness, 0.0);
                    double end = Math.Max(m.BeakerEndWallThickness, 0.0);
                    double hs = Math.Max(m.SourceHeight, 0.0);
                    double zWall = zFace - Math.Max(m.BeakerToDetectorDistance, 0.0);
                    double zSrc = zWall - end;
                    double farEnd = CylinderFarEnd(m);
                    double zFar = zSrc - hs - CylinderHeadspace(m);
                    this.DimX(g, far, 0, -rOut, rOut, zFar - farEnd, zFar - farEnd, 2.0 * rOut, "BeakerDiameter", RankMain);
                    this.WallAtEdge(g, near, -rOut, -(rOut - wall), zSrc - 0.5 * hs, zWall, wall, "BeakerSideWallThickness");
                    this.DimZ(g, DimSide.Right, 0, zSrc - hs, zSrc, rOut, rOut, hs, "SourceHeight", RankMain);

                    // Стенка и расстояние идут подряд: цепочка, им не нужны хвосты через чужие выносные.
                    this.DimZ(g, DimSide.Left, 1, zSrc, zWall, -rOut, -rOut, end, "BeakerEndWallThickness", RankThin);
                    this.DimZ(g, DimSide.Left, 1, zWall, zFace, -rOut, -outerHalf, Math.Max(m.BeakerToDetectorDistance, 0.0), "BeakerToDetectorDistance", RankSecond);

                    // (`f8cyl`) Дальнее дно — за пробой и запасом.
                    if (farEnd > 0.0)
                    {
                        this.DimZ(g, DimSide.Left, 0, zFar - farEnd, zFar, -rOut, -rOut, farEnd, "BeakerFarEndWallThickness", RankThin);
                    }

                    // ⛔ (`E39`) ВЫСОТА СОСУДА ПОДПИСЫВАЕТСЯ ТОЛЬКО ТАМ, ГДЕ СОСУД ЕСТЬ. Сцена «на земле» ставит
                    // `BeakerHeight = SourceHeight`, и чертёж печатал бы два одинаковых числа. Судим НЕ по совпадению
                    // значений (человек мог ввести равные числа руками), а по СТЕНКАМ: у сосуда без стенок высоты нет.
                    if (VesselExists(wall, Math.Max(end, farEnd)))
                    {
                        // (`AMBER205`) Сосуд — с запасом стенки над пробой (и дальним дном).
                        this.DimZ(g, DimSide.Right, 0, zFar - farEnd, zWall, rOut, rOut, Math.Max(m.BeakerHeight, 0.0), "BeakerHeight", RankSecond);
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

                    // Корпус рисуется от zSrc0 - endWall высотой body (см. DrawSource), размер мерит ровно то же.
                    // (`AMBER205`) Верх корпуса — за пустой частью стакана.
                    double zTopOuter = zSrc0 - endWall - MarinelliFarGap(m);
                    double zOpen = zCeiling + hh;
                    this.DimX(g, far, 0, -rOut, rOut, zTopOuter, zTopOuter, 2.0 * rOut, "MarinelliBeakerDiameter", RankMain);

                    // У отверстия: стенка стакана и цепочка колодца. (`AMBER282`) У трапеции диаметра нет: её размеры на виде сверху.
                    this.WallAtEdge(g, near, -rOut, -(rOut - side), zSrc0 + 0.5 * hs, zTopOuter + body, side, "MarinelliSideThickness");
                    if (!m.HasTrapezoidHole)
                    {
                        this.DimX(g, near, 1, -rh, rh, zOpen, zOpen, 2.0 * rh, "MarinelliHoleDiameter", RankMain);
                    }

                    this.DimX(g, near, 1, rh, rhOut, zOpen, zOpen, ths, "MarinelliHoleSideThickness", RankThin);

                    this.DimZ(g, DimSide.Right, 1, zCeiling - the, zCeiling, rh, rh, the, "MarinelliHoleEndWallThickness", RankThin);
                    this.DimZ(g, DimSide.Right, 1, zCeiling, zOpen, rh, rh, hh, "MarinelliHoleHeight", RankSecond);

                    // ⛔ (`E39`) То же правило, что у цилиндра: лунка в грунте стаканом не является.
                    if (VesselExists(side, endWall))
                    {
                        this.DimZ(g, DimSide.Right, 0, zTopOuter, zTopOuter + body, rOut, rOut, body, "MarinelliBeakerHeight", RankMain);
                        this.DimZ(g, DimSide.Left, 1, zTopOuter, zTopOuter + endWall, -rOut, -rOut, endWall, "MarinelliEndWallThickness", RankThin);
                    }

                    this.DimZ(g, DimSide.Left, 1, zSrc0, zSrc0 + hs, -rOut, -rOut, hs, "MarinelliSourceHeight", RankMain);
                    this.DimZ(g, DimSide.Left, 0, zCeiling, zFace, -rh, -outerHalf, Math.Max(m.MarinelliToDetectorDistance, 0.0), "MarinelliToDetectorDistance", RankSecond);
                    return;
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
            this.Dim(g, DimSide.Bottom, 0, cavity.Left, cavity.Right, cavity.Bottom, cavity.Bottom, m.ShieldInnerDiameter, "ShieldInnerDiameter", RankMain);
            this.Dim(g, DimSide.Right, 0, cavity.Top, cavity.Bottom, cavity.Right, cavity.Right, m.ShieldInnerHeight, "ShieldInnerHeight", RankMain);

            // (`AMBER282`, Amber 03.11.2026: «Ошибка в эскизе, внутренняя высота 100, внешняя
            // 160») Как на чертеже — обе высоты: внутренняя и НАРУЖНАЯ целиком: все слои низа,
            // полость и все слои верха. Число честное, даже если полосы укорочены разрывом.
            double floorSum = 0.0, lidSum = 0.0;
            foreach (GeometryShieldLayer layer in m.ShieldLayers)
            {
                floorSum += Math.Max(0.0, layer.FloorThickness);
                lidSum += Math.Max(0.0, layer.LidThickness);
            }

            // (`AMBER324`) Наружная высота — СЛЕВА. Решение Amber 09.11.2026 вопросником, дословно: «Перенести «304» влево
            // (Рекомендую)». Справа стоят размеры пробы и полости; с горизонтальными числами и разнесёнными рядами
            // (`RowOffsets`) третий ряд справа мельчил чертёж «Защиты» до 0.62 в поле 280×427, а слева рядов почти нет.
            this.Dim(g, DimSide.Left, 0, this.FloorEdge(box, n - 1), this.LidEdge(box, n - 1), all.Left, all.Left,
                     floorSum + m.ShieldInnerHeight + lidSum, null, RankSecond);

            if (m.ShieldFloorGap > 0.0)
            {
                this.DimZ(g, DimSide.Left, 0, box.FloorFace, box.FloorFace + m.ShieldFloorGap, -box.Cavity, -box.Cavity,
                          m.ShieldFloorGap, "ShieldFloorGap", RankThin);
            }

            // Толщины слоёв стоят в легенде; размером — только у поля в фокусе.
            for (int i = 0; i < n; i++)
            {
                RectangleF outer = this.ShieldRect(box, i), inner = this.ShieldRect(box, i - 1);
                GeometryShieldLayer layer = m.ShieldLayers[i];
                this.Dim(g, DimSide.Bottom, 0, inner.Right, outer.Right, all.Bottom, all.Bottom, layer.SideThickness,
                         GeometryScenes.ShieldLayerField(i, "Side"), RankFocusOnly);
                this.Dim(g, DimSide.Left, 0, this.FloorEdge(box, i), this.FloorEdge(box, i - 1), all.Left, all.Left, layer.FloorThickness,
                         GeometryScenes.ShieldLayerField(i, "Floor"), RankFocusOnly);
                this.Dim(g, DimSide.Left, 0, this.LidEdge(box, i - 1), this.LidEdge(box, i), all.Left, all.Left, layer.LidThickness,
                         GeometryScenes.ShieldLayerField(i, "Lid"), RankFocusOnly);
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

                // (`AMBER307`) В узком поле хвост «не в масштабе» уходил за рамку: своей строкой.
                this.legendTailApart = legendW > this.Width - 12f;
                if (this.legendTailApart)
                {
                    legendH += this.legendLineHeight;
                }
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
                    bool notToScale = line.Outer && (this.shieldPx != null && this.shieldPx.NotToScale || this.breakPx > 0f);
                    string text = notToScale && !this.legendTailApart ? line.Text + Resources.EfficiencySketchNotToScale : line.Text;
                    this.Print(g, isLit ? lit : (line.Gray ? gray : ink), text, null, x, y, g.MeasureString(text, this.Font));
                    y += this.legendLineHeight;
                    if (notToScale && this.legendTailApart)
                    {
                        string tail = Resources.EfficiencySketchNotToScale.TrimStart(',', ' ');
                        this.Print(g, gray, tail, null, x, y, g.MeasureString(tail, this.Font));
                        y += this.legendLineHeight;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // (`AMBER282`, `AMBER307`, `AMBER324`) Вид со стороны пробы — отдельный эскиз рядом с разрезом:
        // взгляд вдоль оси сцены со стороны пробы на торец детектора. Amber 08.11.2026, к снимку вкладки
        // «Источник», дословно: «Почему вид сверху не со стороны детектора?»; решение вопросником того же
        // дня, дословно: «Со стороны пробы (Рекомендую)» — как смотрит человек на прибор с пробой сверху:
        // проба и сосуд ближе всего к зрителю и видны, детектор за ними. До `AMBER324` вид шёл из-за
        // детектора наружу, и проба на торце была закрыта кристаллом.
        //
        // Что видно и что закрыто (ГОСТ 2.303-68: линия невидимого контура — штриховая):
        //   банка, чашка, кювета, маринелли, грунт, лунка — сосуд и проба поверх прибора; контур корпуса и
        //     кристалла под пробой — штриховой (без него не видно, как проба легла на торец); колодец
        //     маринелли под пробой — тоже штриховой; дальнее донышко и крышка сосуда не рисуются: план — взгляд
        //     в открытый сосуд, как и прежде;
        //   точка — кружок пробы поверх прибора, видимый;
        //   поле — условная окружность вокруг прибора, прибор не закрыт;
        //   защита на плане не рисуется ни на одной вкладке (решение Amber 06.11.2026 по `AMBER307`).
        // Ось X вправо, как на разрезе (проекционная связь); зритель на −z смотрит к +z, поэтому ось Y
        // вниз — вид отражён по Y против прежнего: основание 1 трапеции (y < 0) теперь сверху.
        //
        // (`AMBER325`) Amber 11.11.2026, к снимку вкладки «Источник» (RC-103, прямоугольная банка 40×40, стрелка на
        // штриховой контур прибора под пробой), дословно: «Запиши задачу, нужно переделать скетч на From detector side.».
        // Решения вопросником того же дня, дословно: «Сделай в скетче CheckBox - с какой стороны показывать образец. С верху
        // или с низу. Пускай пользователь решает.»; умолчание — «Со стороны детектора, помнить в настройках (Рекомендую)»;
        // закрытое — «Штрихом, как в виде со стороны пробы (Рекомендую)»; где — «Только «Источник» (Рекомендую)». Значит:
        //   «Источник» — по умолчанию вид со стороны детектора (`DrawPlanFromDetector`): ось Y вверх, прибор поверх сосуда и
        //     пробы, закрытое прибором — штрихом; галка «Со стороны пробы» у заголовка — вид выше, со стороны пробы; выбор —
        //     в главном конфиге (`PlanSideStore`), на все кривые;
        //   «Детектор» — как было: грань кристалла, обращённая к пробе, «Вид со стороны пробы», без галки.
        // Числа горизонтально (`AMBER324`) — в обоих видах.
        // Тем же масштабом, что разрез, и тем же языком: заливки и штриховка слоёв, тонкий слой — не тоньше
        // MinBandPx. Размеры берутся у той же CrystalBoxInScene и теми же толщинами, что сцена счёта:
        // чертёж и расчёт не расходятся.
        // ------------------------------------------------------------------

        /// <summary>Зазор между видами, точек: как в окне импорта GDML.</summary>
        const int ViewGap = 8;

        /// <summary>Уже этого два вида рядом не ставятся: заголовку второго некуда встать.</summary>
        const int SideBySideMinWidth = 360;

        /// <summary>Мельче этого план не читается: остаётся один разрез.</summary>
        const double MinPlanPx = 24.0;

        /// <summary>Уже этого пояснение под заголовком вида не пишется: чертёж важнее.</summary>
        const int HintMinWidth = 300;

        bool legendTailApart;
        bool planView, planStacked;
        double planRadiusX, planRadiusY;
        PointF planCenter;
        Rectangle sidePanel, planPanel;
        float plateX, plateY;

        /// <summary>Рамки видов последней отрисовки. Читает проба.</summary>
        public Rectangle SideViewBounds
        {
            get { return this.sidePanel; }
        }

        public Rectangle TopViewBounds
        {
            get { return this.planPanel; }
        }

        float PX(double x)
        {
            return (float)(this.planCenter.X + x * this.scale);
        }

        float PY(double y)
        {
            // (`AMBER324`) Вид со стороны пробы на торец: ось X вправо, ось Z от зрителя, значит ось Y вниз.
            // (`AMBER325`) Со стороны детектора (умолчание «Источника»): ось Z к зрителю, значит ось Y вверх — как до `AMBER324`.
            return this.PlanFromDetector ? (float)(this.planCenter.Y - y * this.scale) : (float)(this.planCenter.Y + y * this.scale);
        }

        /// <summary>Строка заголовка вида: что напечатать, чем и какое поле её подсвечивает.</summary>
        struct HeaderLine
        {
            public string Text;
            public bool Bold, Gray;
            public string Key;

            /// <summary>(`AMBER325`) На этой строке заголовка — галка стороны вида: 0 — нет, 1 — с подписью, 2 — один квадрат,
            /// 3 — с короткой подписью.</summary>
            public int CheckMode;
        }

        // (`AMBER325`, второй заход по вердикту критика `crit325`) Галка своей строкой (17 точек) стоила чертежу: в поле 198×367
        // «куб бок + трапеция» и «брусок бок + трапеция» план со всеми размерами уже не помещался, и раскладка уходила на уровень 0
        // — без единого размера (замер: строка 8 точек — уже уровень 0; 0 точек — уровень 3, 12 чисел). Поэтому своей строки у
        // галки нет: она стоит В СТРОКЕ ЗАГОЛОВКА второго вида, высоты заголовку не прибавляет, и раскладка чертежа та же, что до
        // `AMBER325`. Помещается подпись — галка с подписью «Со стороны пробы» за заголовком; не помещается (план уже ~300 точек) —
        // один квадрат за заголовком, подпись — во всплывающей подсказке; заголовок рядом называет вид, который сейчас показан.

        /// <summary>(`AMBER325`) Поле квадрата галки с отступом до подписи, точек.</summary>
        const float CheckGlyphPx = 20f;

        /// <summary>(`AMBER325`) Ширина галки без подписи (один квадрат), точек; столько же заголовок уступает при переносе.</summary>
        const int CheckBareWidth = 15;

        /// <summary>(`AMBER325`) Зазор между заголовком и галкой, точек.</summary>
        const float CheckGapPx = 10f;

        /// <summary>(`AMBER325`) Ширина галки с подписью: подпись мерой чертежа, слева поле под квадрат.</summary>
        float CheckWidth(Graphics g, string text)
        {
            return (float)Math.Ceiling(g.MeasureString(text, this.Font).Width + CheckGlyphPx + 4f);
        }

        float LineHeight(Graphics g, HeaderLine line, Font bold)
        {
            return g.MeasureString(line.Text, line.Bold ? bold : this.Font).Height;
        }

        /// <summary>Строки заголовка вида, с переносом по ширине: заголовок, пояснение, у плана — разворот бруска и его глубина.</summary>
        List<HeaderLine> HeaderLines(Graphics g, GeometryModel m, bool top, float width)
        {
            List<HeaderLine> lines = new List<HeaderLine>();
            float wrap = Math.Max(40f, width - 16f);
            // (`AMBER324`) Заголовок тоже переносится: «Вид со стороны пробы» длиннее прежнего «Вид сверху» и в узкой
            // рамке плана (420×460, 360×300 рядом) выходил за неё.
            // (`AMBER325`) На «Источнике» второй вид по умолчанию — со стороны детектора, галкой — со стороны пробы. На «Детекторе»
            // вид прежний: грань кристалла, обращённая к пробе, «Вид со стороны пробы»; галки там нет (решение Amber 11.11.2026
            // «Только «Источник» (Рекомендую)»). Заголовок верен при любой ориентации разреза (`flipZ`): назван не верх и не низ,
            // а сторона, с которой смотрят.
            bool fromDetector = top && this.PlanFromDetector;
            string title = !top ? Resources.EfficiencySketchSideView
                         : fromDetector ? Resources.EfficiencySketchDetectorSideView
                         : Resources.EfficiencySketchTopView;
            bool check = top && this.Mode == SketchMode.Source;
            using (Font bold = new Font(this.Font, FontStyle.Bold))
            {
                // Галка — за последней строкой заголовка: перенос уступает ей место квадрата, чтобы квадрат влез всегда.
                List<string> titleLines = Wrapped(g, title, bold, check ? Math.Max(40f, wrap - CheckBareWidth - 4f) : wrap);
                foreach (string line in titleLines)
                {
                    lines.Add(new HeaderLine { Text = line, Bold = true });
                }

                if (check && lines.Count > 0)
                {
                    HeaderLine last = lines[lines.Count - 1];
                    // (`AMBER325`, третий заход) Amber 11.11.2026 вопросником, дословно: «Короткая подпись в узком (Рекомендую)»:
                    // полная подпись — где влезает; не влезает — короткая в той же строке; не влезает и она — один квадрат.
                    // Квадрат без слова читался «вид выключен» (критик `crit325`).
                    float lastWidth = g.MeasureString(last.Text, bold).Width;
                    bool labelled = titleLines.Count == 1
                                    && 8f + lastWidth + CheckGapPx + this.CheckWidth(g, Resources.EfficiencySketchFromSampleCheck) <= width - 8f;
                    bool shortLabel = !labelled
                                      && 8f + lastWidth + CheckGapPx + this.CheckWidth(g, Resources.EfficiencySketchFromSampleShort) <= width - 8f;
                    last.CheckMode = labelled ? 1 : shortLabel ? 3 : 2;
                    lines[lines.Count - 1] = last;
                }
            }

            if (width >= HintMinWidth)
            {
                // (`AMBER324`) «Невидимый контур — штриховой» — только там, где штрих есть: на «Детекторе» и у точки его нет.
                // (`AMBER325`) Со стороны детектора штрих — у того, что закрыл прибор: точка, малая банка под торцом.
                string look = !top ? Resources.EfficiencySketchSideHint
                            : fromDetector ? Resources.EfficiencySketchDetectorSideHint
                            : Resources.EfficiencySketchTopHint;
                string hint = top && this.PlanHasHidden(m) ? look + "; " + Resources.EfficiencySketchHiddenHint : look;
                foreach (string line in Wrapped(g, hint, this.Font, wrap))
                {
                    lines.Add(new HeaderLine { Text = line, Gray = true });
                }
            }

            if (top)
            {
                lines.AddRange(this.FacingLines(g, m, width));
            }

            return lines;
        }

        /// <summary>E21: развёрнутый брусок на разрезе выглядит как другой кристалл: подпись разворота и размер, ушедший в глубину.</summary>
        List<HeaderLine> FacingLines(Graphics g, GeometryModel m, float width)
        {
            List<HeaderLine> lines = new List<HeaderLine>();
            if (m.Facing != GeometryDetectorFacing.Side)
            {
                return lines;
            }

            foreach (string line in Wrapped(g, Resources.EfficiencySketchFacingSide, this.Font, Math.Max(40f, width - 16f)))
            {
                lines.Add(new HeaderLine { Text = line });
            }

            if (m.Shape == CrystalShape.Box)
            {
                double hx, hy, depth;
                string keyX, keyY, keyDepth;
                m.CrystalBoxInScene(out hx, out hy, out depth, out keyX, out keyY, out keyDepth);
                lines.Add(new HeaderLine
                {
                    Text = string.Format(CultureInfo.InvariantCulture, Resources.EfficiencySketchDepth, Format(depth), Resources.GeometryEditorUnitMm),
                    Key = keyDepth,
                });
            }

            return lines;
        }

        /// <summary>Перенос по словам мерой той же, какой текст печатается.</summary>
        static List<string> Wrapped(Graphics g, string text, Font font, float width)
        {
            List<string> lines = new List<string>();
            string current = "";
            foreach (string word in text.Split(' '))
            {
                string next = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && g.MeasureString(next, font).Width > width)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = next;
                }
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }

            return lines;
        }

        float HeaderHeight(Graphics g, GeometryModel m, bool top, float width)
        {
            float height = 6f;
            using (Font bold = new Font(this.Font, FontStyle.Bold))
            {
                foreach (HeaderLine line in this.HeaderLines(g, m, top, width))
                {
                    height += this.LineHeight(g, line, bold);
                }
            }

            return height + 2f;
        }

        /// <summary>Заголовки видов — до размеров: раскладка чисел их обходит.</summary>
        void DrawViewTitles(Graphics g, GeometryModel m)
        {
            if (!this.planView)
            {
                // Плана нет, а подпись разворота и глубина всё равно нужны: на разрезе брусок читается как другой кристалл.
                if (this.Mode != SketchMode.Overview)
                {
                    float at = 6f;
                    using (Brush brush = new SolidBrush(Ink))
                    using (Brush lit = new SolidBrush(LitColor))
                    {
                        foreach (HeaderLine line in this.FacingLines(g, m, this.Width))
                        {
                            SizeF size = g.MeasureString(line.Text, this.Font);
                            this.Print(g, this.Lit(line.Key) ? lit : brush, line.Text, line.Key, 8f, at, size);
                            at += size.Height;
                        }
                    }
                }

                return;
            }

            foreach (bool top in new[] { false, true })
            {
                Rectangle panel = top ? this.planPanel : this.sidePanel;
                float y = panel.Top + 6f;
                using (Font bold = new Font(this.Font, FontStyle.Bold))
                using (Brush ink = new SolidBrush(Ink))
                using (Brush gray = new SolidBrush(Color.FromArgb(0x4C, 0x4A, 0x44)))
                using (Brush lit = new SolidBrush(LitColor))
                {
                    foreach (HeaderLine line in this.HeaderLines(g, m, top, panel.Width))
                    {
                        Font font = line.Bold ? bold : this.Font;
                        SizeF size = g.MeasureString(line.Text, font);
                        this.Print(g, font, this.Lit(line.Key) ? lit : (line.Gray ? gray : ink), line.Text, line.Key, panel.Left + 8f, y, size);
                        if (line.CheckMode != 0)
                        {
                            // (`AMBER325`) Галку рисует сама галка (`PlacePlanSideCheck`). В подписях — её рисунок: квадрат и
                            // подпись в строке заголовка (раскладка чисел их обходит, строку они не раздвигают).
                            // (`AMBER325`, четвёртый заход, вердикт `crit325`) Рамка самой галки — по её `PreferredSize` (18
                            // точек при шрифте 13): в рамке высотой строки (13) галка срезала хвосты «р», «p» — «от ппобы»,
                            // «from samole». Рамка выше строки на ~2 точки сверху и снизу, по центру строки; фон галки прозрачный,
                            // поэтому соседняя строка заголовка под её полями видна. Текст и квадрат галки (шрифт 13 точек)
                            // в строку помещаются.
                            string text = Resources.EfficiencySketchFromSampleCheck;
                            string shown = line.CheckMode == 1 ? text : line.CheckMode == 3 ? Resources.EfficiencySketchFromSampleShort : "";
                            float x = panel.Left + 8f + size.Width + (line.CheckMode == 2 ? 4f : CheckGapPx);
                            int w = line.CheckMode == 2 ? CheckBareWidth : (int)this.CheckWidth(g, shown);
                            int rowTop = (int)Math.Ceiling(y), rowBottom = (int)Math.Floor(y + size.Height);
                            Size preferred = this.CheckPreferredSize(shown);
                            int h = Math.Max(preferred.Height, rowBottom - rowTop);
                            int checkTop = (int)Math.Round(y + 0.5f * size.Height - 0.5f * h);
                            this.planCheckRect = new Rectangle((int)Math.Round(x), checkTop, Math.Max(w, preferred.Width), h);
                            this.planCheckBare = line.CheckMode == 2;
                            this.planCheckShort = line.CheckMode == 3;
                            this.labels.Add(new SketchLabel
                            {
                                Bounds = new Rectangle(this.planCheckRect.X, rowTop, this.planCheckRect.Width, Math.Max(1, rowBottom - rowTop)),
                                Text = text,
                            });
                        }

                        y += size.Height;
                    }
                }
            }
        }

        /// <summary>
        /// Что занимает план, мм от оси: прибор с обвязкой, проба, сосуд, полость. Половины по x и y
        /// порознь: у бруска они разные, и квадрат под ним съедал бы чертёж.
        /// </summary>
        void PlanExtent(GeometryModel m, double zFace, out double rx, out double ry)
        {
            double hx, hy, tsr, tsg, tsc;
            this.PlanCrystal(m, out hx, out hy, out tsr, out tsg, out tsc);
            double wrap = tsr + tsg + tsc;
            rx = hx + wrap;
            ry = hy + wrap;
            if (this.Mode != SketchMode.Source)
            {
                return;
            }

            double reachX = 0.0, reachY = 0.0;
            if (m.Scene == GeometrySceneKind.Iso)
            {
                double zc, rr;
                FieldRing(m, zFace, out zc, out rr);
                reachX = reachY = rr;
            }
            else
            {
                switch (m.SourceType)
                {
                    case GeometrySourceType.Marinelli:
                        reachX = reachY = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                        break;
                    case GeometrySourceType.Cylinder:
                        reachX = reachY = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                        break;
                    case GeometrySourceType.Box:
                        reachX = 0.5 * Math.Max(m.BoxSourceX, 0.0);
                        reachY = 0.5 * Math.Max(m.BoxSourceY, 0.0);
                        break;
                }
            }

            rx = Math.Max(rx, reachX);
            ry = Math.Max(ry, reachY);
        }

        /// <summary>
        /// Кристалл и обвязка на плане — ровно те числа, что строит сцена счёта
        /// (`EfficiencySimulator.Build`): половины из <see cref="GeometryModel.CrystalBoxInScene"/>,
        /// боковые толщины с той же перестановкой при боковой постановке.
        /// </summary>
        void PlanCrystal(GeometryModel m, out double hx, out double hy, out double tsr, out double tsg, out double tsc)
        {
            if (m.Shape == CrystalShape.Box)
            {
                double depth;
                m.CrystalBoxInScene(out hx, out hy, out depth);
                hx = Math.Max(hx, 0.0);
                hy = Math.Max(hy, 0.0);
            }
            else
            {
                hx = hy = 0.5 * Math.Max(m.CrystalDiameter, 0.0);
            }

            bool side = m.Facing == GeometryDetectorFacing.Side;
            tsr = Math.Max(side ? m.FrontReflectorThickness : m.SideReflectorThickness, 0.0);
            tsg = Math.Max(side ? m.FrontGapThickness : m.SideGapThickness, 0.0);
            tsc = Math.Max(side ? m.FrontCladdingThickness : m.SideCladdingThickness, 0.0);
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

            if (this.Mode == SketchMode.Detector)
            {
                this.DrawPlanDetector(g, m);
                return;
            }

            if (this.PlanFromDetector)
            {
                this.DrawPlanFromDetector(g, m);
                return;
            }

            using (Pen ink = new Pen(Ink, 1.2f))
            {
                // (`AMBER324`) Взгляд со стороны пробы: прибор дальше всего от зрителя и рисуется первым,
                // сосуд и проба поверх него. Что проба закрыла — штриховым невидимым контуром в конце.
                this.DrawPlanDetector(g, m);
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

                bool field = m.Scene == GeometrySceneKind.Iso;
                bool covered = false;
                float hiddenDrawn = float.MaxValue;
                switch (field ? GeometrySourceType.Point : m.SourceType)
                {
                    case GeometrySourceType.Marinelli:
                    {
                        double rOut = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                        double rh = 0.5 * Math.Max(m.MarinelliHoleDiameter, 0.0);
                        double ths = Math.Max(m.MarinelliHoleSideThickness, 0.0);
                        double rSrc = trap ? Math.Max(holeOuterReach, rOut - Math.Max(m.MarinelliSideThickness, 0.0))
                                           : Math.Max(rh + ths, rOut - Math.Max(m.MarinelliSideThickness, 0.0));
                        float beakerPx = (float)(rOut * this.scale);
                        this.FillCircle(g, WallColor, beakerPx);
                        this.FillCircle(g, SampleColor, (float)(rSrc * this.scale));

                        // Над колодцем лежит слой пробы, если проба выше колодца (`cap` разреза); иначе сверху
                        // видно донышко колодца — цветом стенки. Сам колодец и прибор в нём закрыты.
                        bool cap = Math.Max(m.MarinelliSourceHeight, 0.0) > Math.Max(m.MarinelliHoleHeight, 0.0);
                        if (!cap)
                        {
                            if (trap)
                            {
                                using (Brush wall = new SolidBrush(WallColor))
                                {
                                    g.FillPolygon(wall, this.PlanPoints(xo, yo));
                                }
                            }
                            else
                            {
                                this.FillCircle(g, WallColor, (float)((rh + ths) * this.scale));
                            }
                        }

                        this.Circle(g, ink, beakerPx);
                        using (Pen hidden = HiddenPen())
                        {
                            if (trap)
                            {
                                g.DrawPolygon(hidden, this.PlanPoints(xi, yi));
                                hiddenDrawn = (float)(Math.Min(Math.Abs(yi[3] - yi[0]), Math.Min(Math.Abs(xi[1] - xi[0]), Math.Abs(xi[2] - xi[3]))) * 0.5 * this.scale);
                            }
                            else if (rh * this.scale >= HiddenMinPx)
                            {
                                this.Circle(g, hidden, (float)(rh * this.scale));
                                hiddenDrawn = (float)(rh * this.scale);
                            }
                            else if (rh * this.scale >= 1.5)
                            {
                                // (`AMBER324`) Мелкий колодец (лунка в поле 280×427, радиус 2–5 точек) — мелким штрихом: знак
                                // колодца на плане остаётся, а прибор в нём уже не рисуется (`DrawPlanDetectorHidden`).
                                using (Pen fine = new Pen(Ink, 1f) { DashStyle = DashStyle.Custom, DashPattern = new[] { 2f, 1.5f } })
                                {
                                    this.Circle(g, fine, (float)(rh * this.scale));
                                }

                                hiddenDrawn = (float)(rh * this.scale);
                            }
                        }

                        covered = true;
                        break;
                    }

                    case GeometrySourceType.Cylinder:
                    {
                        double rOut = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                        float beakerPx = (float)(rOut * this.scale);
                        this.FillCircle(g, WallColor, beakerPx);
                        this.FillCircle(g, SampleColor, (float)(Math.Max(0.0, rOut - m.BeakerSideWallThickness) * this.scale));
                        this.Circle(g, ink, beakerPx);
                        covered = true;
                        break;
                    }

                    case GeometrySourceType.Box:
                    {
                        double hx = 0.5 * Math.Max(m.BoxSourceX, 0.0), hy = 0.5 * Math.Max(m.BoxSourceY, 0.0);
                        double w = Math.Max(m.BoxSideWallThickness, 0.0);
                        using (Brush wall = new SolidBrush(WallColor))
                        using (Brush sample = new SolidBrush(SampleColor))
                        {
                            g.FillRectangle(wall, this.PlanRect(hx, hy));
                            g.FillRectangle(sample, this.PlanRect(Math.Max(0.0, hx - w), Math.Max(0.0, hy - w)));
                        }

                        RectangleF outline = this.PlanRect(hx, hy);
                        g.DrawRectangle(ink, outline.X, outline.Y, outline.Width, outline.Height);
                        covered = true;
                        break;
                    }

                    default:
                        if (field)
                        {
                            // Поле: та же условная окружность, что на разрезе (`FieldRing`), не в масштабе.
                            double zc, rr;
                            FieldRing(m, -(this.ToSample(m)), out zc, out rr);
                            float pr = (float)(rr * this.scale);
                            using (Pen ring = new Pen(SampleColor, 1.2f) { DashStyle = DashStyle.Dash })
                            {
                                this.Circle(g, ring, pr);
                            }

                            using (Pen arrow = new Pen(SampleColor, 1.4f))
                            {
                                arrow.CustomEndCap = new AdjustableArrowCap(4f, 6f);
                                for (int k = 0; k < 8; k++)
                                {
                                    double a = k * Math.PI / 4.0;
                                    float ex = (float)Math.Cos(a), ey = (float)Math.Sin(a);
                                    g.DrawLine(arrow, this.planCenter.X + ex * pr, this.planCenter.Y + ey * pr,
                                               this.planCenter.X + ex * pr * 0.72f, this.planCenter.Y + ey * pr * 0.72f);
                                }
                            }
                        }
                        else
                        {
                            // Точка на оси перед торцом: со стороны пробы она ближе прибора и видна — кружок пробы,
                            // тем же размером, что на разрезе.
                            using (Brush sample = new SolidBrush(SampleColor))
                            {
                                g.FillEllipse(sample, this.planCenter.X - 5f, this.planCenter.Y - 5f, 10f, 10f);
                            }

                            g.DrawEllipse(ink, this.planCenter.X - 5f, this.planCenter.Y - 5f, 10f, 10f);
                        }

                        break;
                }

                if (covered)
                {
                    this.DrawPlanDetectorHidden(g, m, hiddenDrawn);
                }
            }
        }

        RectangleF CircleRect(float r)
        {
            return new RectangleF(this.planCenter.X - r, this.planCenter.Y - r, 2f * r, 2f * r);
        }

        /// <summary>
        /// (`AMBER325`) Вид со стороны детектора — умолчание «Источника»: зритель за прибором смотрит вдоль оси на пробу. Прибор
        /// ближе всего и рисуется последним, поверх сосуда и пробы, — как до `AMBER324` (ось Y вверх, трапеция основанием 2
        /// сверху). Что прибор закрыл — контур сосуда, колодца маринелли, точку на оси — штриховым невидимым контуром поверх
        /// прибора (ГОСТ 2.303-68); решение Amber 11.11.2026 вопросником, дословно: «Штрихом, как в виде со стороны пробы
        /// (Рекомендую)». Штрих отсечён по наружному контуру прибора: где контур сосуда виден мимо прибора, он сплошной.
        /// Дальнее донышко сосуда, как и прежде, не рисуется: план — взгляд в открытый сосуд.
        /// </summary>
        void DrawPlanFromDetector(Graphics g, GeometryModel m)
        {
            using (Pen ink = new Pen(Ink, 1.2f))
            using (GraphicsPath hidden = new GraphicsPath())
            {
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

                bool field = m.Scene == GeometrySceneKind.Iso;
                bool point = false;
                switch (field ? GeometrySourceType.Point : m.SourceType)
                {
                    case GeometrySourceType.Marinelli:
                    {
                        double rOut = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                        double rh = 0.5 * Math.Max(m.MarinelliHoleDiameter, 0.0);
                        double ths = Math.Max(m.MarinelliHoleSideThickness, 0.0);
                        double rSrc = trap ? Math.Max(holeOuterReach, rOut - Math.Max(m.MarinelliSideThickness, 0.0))
                                           : Math.Max(rh + ths, rOut - Math.Max(m.MarinelliSideThickness, 0.0));
                        float beakerPx = (float)(rOut * this.scale);
                        this.FillCircle(g, WallColor, beakerPx);
                        this.FillCircle(g, SampleColor, (float)(rSrc * this.scale));

                        // Колодец открыт к детектору: стенка колодца и его просвет, контур колодца сплошной там, где виден.
                        if (trap)
                        {
                            using (Brush wall = new SolidBrush(WallColor))
                            using (Brush canvas = new SolidBrush(Canvas))
                            {
                                g.FillPolygon(wall, this.PlanPoints(xo, yo));
                                g.FillPolygon(canvas, this.PlanPoints(xi, yi));
                            }

                            g.DrawPolygon(ink, this.PlanPoints(xi, yi));
                            hidden.AddPolygon(this.PlanPoints(xi, yi));
                        }
                        else
                        {
                            float holePx = (float)(rh * this.scale);
                            this.FillCircle(g, WallColor, (float)((rh + ths) * this.scale));
                            this.FillCircle(g, Canvas, holePx);
                            this.Circle(g, ink, holePx);
                            if (holePx >= HiddenMinPx)
                            {
                                hidden.AddEllipse(this.CircleRect(holePx));
                            }
                        }

                        this.Circle(g, ink, beakerPx);
                        if (beakerPx >= HiddenMinPx)
                        {
                            hidden.AddEllipse(this.CircleRect(beakerPx));
                        }

                        break;
                    }

                    case GeometrySourceType.Cylinder:
                    {
                        double rOut = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                        float beakerPx = (float)(rOut * this.scale);
                        this.FillCircle(g, WallColor, beakerPx);
                        this.FillCircle(g, SampleColor, (float)(Math.Max(0.0, rOut - m.BeakerSideWallThickness) * this.scale));
                        this.Circle(g, ink, beakerPx);
                        if (beakerPx >= HiddenMinPx)
                        {
                            hidden.AddEllipse(this.CircleRect(beakerPx));
                        }

                        break;
                    }

                    case GeometrySourceType.Box:
                    {
                        double hx = 0.5 * Math.Max(m.BoxSourceX, 0.0), hy = 0.5 * Math.Max(m.BoxSourceY, 0.0);
                        double w = Math.Max(m.BoxSideWallThickness, 0.0);
                        using (Brush wall = new SolidBrush(WallColor))
                        using (Brush sample = new SolidBrush(SampleColor))
                        {
                            g.FillRectangle(wall, this.PlanRect(hx, hy));
                            g.FillRectangle(sample, this.PlanRect(Math.Max(0.0, hx - w), Math.Max(0.0, hy - w)));
                        }

                        RectangleF outline = this.PlanRect(hx, hy);
                        g.DrawRectangle(ink, outline.X, outline.Y, outline.Width, outline.Height);
                        hidden.AddRectangle(outline);
                        break;
                    }

                    default:
                        if (field)
                        {
                            // Поле: та же условная окружность, что на разрезе (`FieldRing`), не в масштабе; прибор её не закрывает.
                            double zc, rr;
                            FieldRing(m, -(this.ToSample(m)), out zc, out rr);
                            float pr = (float)(rr * this.scale);
                            using (Pen ring = new Pen(SampleColor, 1.2f) { DashStyle = DashStyle.Dash })
                            {
                                this.Circle(g, ring, pr);
                            }

                            using (Pen arrow = new Pen(SampleColor, 1.4f))
                            {
                                arrow.CustomEndCap = new AdjustableArrowCap(4f, 6f);
                                for (int k = 0; k < 8; k++)
                                {
                                    double a = k * Math.PI / 4.0;
                                    float ex = (float)Math.Cos(a), ey = (float)Math.Sin(a);
                                    g.DrawLine(arrow, this.planCenter.X + ex * pr, this.planCenter.Y + ey * pr,
                                               this.planCenter.X + ex * pr * 0.72f, this.planCenter.Y + ey * pr * 0.72f);
                                }
                            }
                        }
                        else
                        {
                            point = true;
                        }

                        break;
                }

                this.DrawPlanDetector(g, m);

                using (Pen pen = HiddenPen())
                {
                    if (point)
                    {
                        // Точка на оси перед торцом: со стороны детектора прибор её закрывает — штриховой кружок поверх прибора,
                        // тем же размером, что на разрезе.
                        g.DrawEllipse(pen, this.planCenter.X - 5f, this.planCenter.Y - 5f, 10f, 10f);
                    }
                    else if (hidden.PointCount > 0)
                    {
                        double hx, hy, tsr, tsg, tsc;
                        this.PlanCrystal(m, out hx, out hy, out tsr, out tsg, out tsc);
                        double wrap = tsr + tsg + tsc;
                        GraphicsState state = g.Save();
                        using (GraphicsPath cover = new GraphicsPath())
                        {
                            RectangleF body = this.PlanRect(hx + wrap, hy + wrap);
                            if (m.Shape == CrystalShape.Box)
                            {
                                cover.AddRectangle(body);
                            }
                            else
                            {
                                cover.AddEllipse(body);
                            }

                            g.SetClip(cover, CombineMode.Intersect);
                            g.DrawPath(pen, hidden);
                        }

                        g.Restore(state);
                    }
                }
            }
        }

        /// <summary>
        /// (`AMBER325`) Закрывает ли прибор со стороны детектора что-нибудь из контуров пробы: точку на оси — всегда; контур
        /// сосуда, колодца маринелли, кюветы — если хоть часть его лежит под наружным контуром прибора (банка ⌀40 на торце
        /// AS 80x80 — вся). Тогда на плане есть штрих, и пояснение вида о нём говорит. Поле прибор не закрывает.
        /// </summary>
        bool DetectorSideHidden(GeometryModel m)
        {
            if (m.Scene == GeometrySceneKind.Iso)
            {
                return false;
            }

            if (m.SourceType == GeometrySourceType.Point)
            {
                return true;
            }

            Func<double, double, bool> under = (x, y) => this.UnderDetector(m, x, y);
            Func<double, bool> circle = r =>
            {
                for (int i = 0; i < 720 && r > 0.0; i++)
                {
                    double a = i * Math.PI / 360.0;
                    if (under(r * Math.Cos(a), r * Math.Sin(a)))
                    {
                        return true;
                    }
                }

                return false;
            };
            Func<double[], double[], bool> polygon = (xs, ys) =>
            {
                for (int i = 0; i < xs.Length; i++)
                {
                    int j = (i + 1) % xs.Length;
                    for (int k = 0; k <= 100; k++)
                    {
                        double t = k / 100.0;
                        if (under(xs[i] + t * (xs[j] - xs[i]), ys[i] + t * (ys[j] - ys[i])))
                        {
                            return true;
                        }
                    }
                }

                return false;
            };

            switch (m.SourceType)
            {
                case GeometrySourceType.Marinelli:
                {
                    double[] xi = new double[4], yi = new double[4];
                    bool hole = m.HasTrapezoidHole && m.TrapezoidHoleVertices(0.0, xi, yi)
                                ? polygon(xi, yi)
                                : circle(0.5 * Math.Max(m.MarinelliHoleDiameter, 0.0));
                    return hole || circle(0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0));
                }

                case GeometrySourceType.Cylinder:
                    return circle(0.5 * Math.Max(m.BeakerDiameter, 0.0));
                case GeometrySourceType.Box:
                {
                    double ax = 0.5 * Math.Max(m.BoxSourceX, 0.0), ay = 0.5 * Math.Max(m.BoxSourceY, 0.0);
                    return ax > 0.0 && ay > 0.0 && polygon(new[] { -ax, ax, ax, -ax }, new[] { -ay, -ay, ay, ay });
                }

                default:
                    return false;
            }
        }

        /// <summary>(`AMBER324`) Закрывает ли проба на плане что-нибудь: банка, кювета, маринелли (с грунтом и лункой). Тогда на
        /// плане есть штриховой невидимый контур, и пояснение вида о нём говорит.</summary>
        bool PlanHasHidden(GeometryModel m)
        {
            if (this.PlanFromDetector)
            {
                return this.DetectorSideHidden(m);
            }

            bool round;
            double ax, ay;
            if (this.Mode != SketchMode.Source || !PlanCover(m, out round, out ax, out ay))
            {
                return false;
            }

            if (m.SourceType == GeometrySourceType.Marinelli)
            {
                return true;
            }

            // (`AMBER324`) Банка ⌀40 на торце AS 80x80 закрывает торец, но не край кристалла: штриха нет — нет и пояснения.
            double hx, hy, tsr, tsg, tsc;
            this.PlanCrystal(m, out hx, out hy, out tsr, out tsg, out tsc);
            bool box = m.Shape == CrystalShape.Box;
            foreach (double wrap in new[] { tsr + tsg + tsc, 0.0 })
            {
                double h1 = hx + wrap, h2 = hy + wrap;
                bool inside = round ? (box ? Math.Min(h1, h2) : h1) < ax
                                    : box ? h1 < ax || h2 < ay : h1 < Math.Max(ax, ay);
                if (inside)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>(`AMBER324`) Что на плане закрывает прибор: сосуд банки и маринелли — круг, кюветы — прямоугольник; мм.</summary>
        static bool PlanCover(GeometryModel m, out bool round, out double ax, out double ay)
        {
            round = true;
            ax = ay = 0.0;
            if (m.Scene == GeometrySceneKind.Iso)
            {
                return false;
            }

            switch (m.SourceType)
            {
                case GeometrySourceType.Marinelli:
                    ax = ay = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                    return ax > 0.0;
                case GeometrySourceType.Cylinder:
                    ax = ay = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                    return ax > 0.0;
                case GeometrySourceType.Box:
                    round = false;
                    ax = 0.5 * Math.Max(m.BoxSourceX, 0.0);
                    ay = 0.5 * Math.Max(m.BoxSourceY, 0.0);
                    return ax > 0.0 && ay > 0.0;
                default:
                    return false;
            }
        }

        /// <summary>(`AMBER324`) Невидимые контуры ближе этого друг к другу, точек, сливаются в каракулю: внутренний не рисуется.</summary>
        const float HiddenSpacingPx = 3f;

        /// <summary>(`AMBER324`) Штриховая окружность мельче этого радиуса, точек, — два-три штриха, не контур: не рисуется.</summary>
        const float HiddenMinPx = 5f;

        /// <summary>(`AMBER324`) Перо невидимого контура: штриховая той же толщины, что видимый контур плана.</summary>
        static Pen HiddenPen()
        {
            return new Pen(Ink, 1.2f) { DashStyle = DashStyle.Custom, DashPattern = new[] { 4f, 2.5f } };
        }

        /// <summary>
        /// (`AMBER324`) Контур корпуса и кристалла под пробой — штриховой (ГОСТ 2.303-68, невидимый контур): без него не
        /// видно, как проба легла на торец. Где прибор не закрыт, штрих ложится на сплошной контур и с ним сливается.
        /// </summary>
        void DrawPlanDetectorHidden(Graphics g, GeometryModel m, float outer)
        {
            double hx, hy, tsr, tsg, tsc;
            this.PlanCrystal(m, out hx, out hy, out tsr, out tsg, out tsc);
            bool box = m.Shape == CrystalShape.Box;
            bool round;
            double ax, ay;
            if (!PlanCover(m, out round, out ax, out ay))
            {
                return;
            }

            // (`AMBER324`) Штрих — только под сосудом и пробой: где край прибора виден, он остаётся сплошным
            // (банка ⌀40 на торце AS 80x80: прежде штрих ложился поверх видимого края кристалла).
            GraphicsState state = g.Save();
            using (GraphicsPath cover = new GraphicsPath())
            using (Pen pen = HiddenPen())
            {
                RectangleF area = this.PlanRect(ax, ay);
                if (round)
                {
                    cover.AddEllipse(area);
                }
                else
                {
                    cover.AddRectangle(area);
                }

                g.SetClip(cover, CombineMode.Intersect);
                // Снаружи внутрь; контур ближе `HiddenSpacingPx` к уже нарисованному (колодцу, корпусу) не рисуется: в мелком
                // масштабе (лунка в поле 280×427) три штриховые окружности сливались в одну каракулю.
                foreach (double wrap in new[] { tsr + tsg + tsc, 0.0 })
                {
                    float half = (float)(Math.Min(hx + wrap, hy + wrap) * this.scale);
                    if (half < HiddenMinPx || outer - half < HiddenSpacingPx)
                    {
                        continue;
                    }

                    outer = half;
                    RectangleF rect = this.PlanRect(hx + wrap, hy + wrap);
                    if (box)
                    {
                        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    }
                    else
                    {
                        g.DrawEllipse(pen, rect);
                    }
                }
            }

            g.Restore(state);
        }

        /// <summary>Сколько обвязки между пробой и кристаллом: тем же правилом, что `zFace` в отрисовке.</summary>
        double ToSample(GeometryModel m)
        {
            double toSample, lateral;
            GeometryScenes.WrappingInScene(m, out toSample, out lateral);
            return toSample;
        }

        /// <summary>
        /// Прибор на плане: корпус, зазор, отражатель, кристалл снаружи внутрь, цветами и порядком разреза.
        /// Брусок при боковой постановке — той гранью, что смотрит на пробу.
        /// </summary>
        void DrawPlanDetector(Graphics g, GeometryModel m)
        {
            double hx, hy, tsr, tsg, tsc;
            this.PlanCrystal(m, out hx, out hy, out tsr, out tsg, out tsc);
            bool box = m.Shape == CrystalShape.Box;
            Action<Color, double> layer = (color, wrap) =>
            {
                using (Brush brush = new SolidBrush(color))
                {
                    RectangleF rect = this.PlanRect(hx + wrap, hy + wrap);
                    if (box)
                    {
                        g.FillRectangle(brush, rect);
                    }
                    else
                    {
                        g.FillEllipse(brush, rect);
                    }
                }
            };
            layer(CladdingColor, tsr + tsg + tsc);
            layer(GapColor, tsr + tsg);
            layer(ReflectorColor, tsr);
            layer(CrystalColor, 0.0);
            using (Pen pen = new Pen(Ink, 1.2f))
            {
                foreach (double wrap in new[] { tsr + tsg + tsc, 0.0 })
                {
                    RectangleF rect = this.PlanRect(hx + wrap, hy + wrap);
                    if (box)
                    {
                        g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                    }
                    else
                    {
                        g.DrawEllipse(pen, rect);
                    }
                }
            }
        }

        RectangleF PlanRect(double halfX, double halfY)
        {
            // (`AMBER325`) Ось Y плана смотрит вверх или вниз по стороне вида: верх рамки — меньшая из двух.
            float y0 = this.PY(-halfY), y1 = this.PY(halfY);
            return RectangleF.FromLTRB(this.PX(-halfX), Math.Min(y0, y1), this.PX(halfX), Math.Max(y0, y1));
        }

        /// <summary>Размеры плана: стороны и диаметры, которых нет на разрезе, в те же ряды по тем же правилам.</summary>
        void QueuePlanDimensions(Graphics g, GeometryModel m)
        {
            if (this.Mode == SketchMode.Detector)
            {
                this.QueuePlanCrystal(g, m);
                return;
            }

            if (m.Scene != GeometrySceneKind.Iso)
            {
                switch (m.SourceType)
                {
                    case GeometrySourceType.Marinelli:
                    {
                        double rOut = 0.5 * Math.Max(m.MarinelliBeakerDiameter, 0.0);
                        this.CircleDim(g, m, rOut, m.MarinelliBeakerDiameter, "MarinelliBeakerDiameter");
                        double[] xi = new double[4], yi = new double[4];
                        if (m.HasTrapezoidHole && m.TrapezoidHoleVertices(0.0, xi, yi)
                            && !(this.PlanFromDetector && Enumerable.Range(0, 4).All(i => this.UnderDetector(m, xi[i], yi[i]))))
                        {
                            // (`AMBER324`) Основание 1 лежит при y < 0, на плане сверху: со стороны пробы ось Y смотрит вниз.
                            // (`AMBER325`) Со стороны детектора ось Y вверх: основание 1 снизу, основание 2 сверху.
                            bool up = this.PlanFromDetector;
                            this.Dim(g, up ? DimSide.Bottom : DimSide.Top, 0, this.PX(xi[0]), this.PX(xi[1]), this.PY(yi[0]), this.PY(yi[1]), m.MarinelliHoleBase1, "MarinelliHoleBase1", RankMain);
                            this.Dim(g, up ? DimSide.Top : DimSide.Bottom, 0, this.PX(xi[3]), this.PX(xi[2]), this.PY(yi[3]), this.PY(yi[2]), m.MarinelliHoleBase2, "MarinelliHoleBase2", RankMain);
                            this.Dim(g, DimSide.Left, 0, this.PY(yi[0]), this.PY(yi[3]), this.PX(xi[0]), this.PX(xi[3]), m.MarinelliHoleWidth, "MarinelliHoleWidth", RankMain);
                        }

                        // (`AMBER324`) Круглый колодец со стороны пробы закрыт, и его ⌀ стоит на разрезе, где колодец виден: к
                        // невидимому контуру размер ставят, только если элемент нельзя образмерить там, где он виден. Основания
                        // и ширина трапеции есть только здесь — они остаются.

                        break;
                    }

                    case GeometrySourceType.Cylinder:
                    {
                        double rOut = 0.5 * Math.Max(m.BeakerDiameter, 0.0);
                        this.CircleDim(g, m, rOut, m.BeakerDiameter, "BeakerDiameter");
                        break;
                    }

                    case GeometrySourceType.Box:
                    {
                        double hx = 0.5 * Math.Max(m.BoxSourceX, 0.0), hy = 0.5 * Math.Max(m.BoxSourceY, 0.0);
                        // (`AMBER325`) Кювета целиком под прибором (угол под прибором — значит и весь контур: прибор выпуклый и
                        // стоит на оси) — размеров на плане нет, они на разрезе; иначе углы, к которым идут выносные, видны.
                        if (this.PlanFromDetector && this.UnderDetector(m, hx, hy))
                        {
                            break;
                        }

                        float edge = this.PlanRect(hx, hy).Top;
                        this.Dim(g, DimSide.Top, 0, this.PX(-hx), this.PX(hx), edge, edge, m.BoxSourceX, "BoxSourceX", RankMain);
                        this.Dim(g, DimSide.Left, 0, this.PY(hy), this.PY(-hy), this.PX(-hx), this.PX(-hx), m.BoxSourceY, "BoxSourceY", RankMain);
                        break;
                    }

                    default:
                        this.QueuePlanCrystal(g, m);
                        break;
                }
            }
        }

        /// <summary>
        /// (`AMBER325`, второй заход; правило `AMBER324`: к невидимому контуру размер ставят, только если элемент нельзя
        /// образмерить там, где он виден) ⌀ сосуда на плане. Со стороны пробы и где края сосуда по оси X видны — как прежде,
        /// горизонтально через центр. Со стороны детектора контур целиком под прибором (банка ⌀40 на торце AS 80x80) — размера на
        /// плане нет, ⌀ стоит на разрезе. Края по X закрыты, а по Y видны (круг шире узкой стороны бруска) — ⌀ вертикально, к
        /// видимым краям.
        /// </summary>
        void CircleDim(Graphics g, GeometryModel m, double r, double value, string key)
        {
            float cx = this.planCenter.X, cy = this.planCenter.Y;
            if (this.PlanFromDetector && this.UnderDetector(m, r, 0.0))
            {
                if (!this.UnderDetector(m, 0.0, r))
                {
                    this.Dim(g, DimSide.Left, 0, this.PY(r), this.PY(-r), cx, cx, value, key, RankMain);
                }

                return;
            }

            this.Dim(g, DimSide.Top, 0, this.PX(-r), this.PX(r), cy, cy, value, key, RankMain);
        }

        /// <summary>(`AMBER325`) Точка плана (мм от оси) под наружным контуром прибора — со стороны детектора она закрыта.</summary>
        bool UnderDetector(GeometryModel m, double x, double y)
        {
            double hx, hy, tsr, tsg, tsc;
            this.PlanCrystal(m, out hx, out hy, out tsr, out tsg, out tsc);
            double ox = hx + tsr + tsg + tsc - 1e-6, oy = hy + tsr + tsg + tsc - 1e-6;
            return m.Shape == CrystalShape.Box ? Math.Abs(x) < ox && Math.Abs(y) < oy : x * x + y * y < ox * ox;
        }

        /// <summary>
        /// Размеры прибора на плане: стороны грани, обращённой к пробе, с ключами полей, попавших на эти стороны
        /// после разворота. Толщины обвязки стоят на разрезе и только там.
        /// </summary>
        void QueuePlanCrystal(Graphics g, GeometryModel m)
        {
            double hx, hy, tsr, tsg, tsc;
            this.PlanCrystal(m, out hx, out hy, out tsr, out tsg, out tsc);
            bool box = m.Shape == CrystalShape.Box;
            string keyX = "CrystalDiameter", keyY = null, keyDepth = null;
            if (box)
            {
                double ax, ay, depth;
                m.CrystalBoxInScene(out ax, out ay, out depth, out keyX, out keyY, out keyDepth);
            }

            RectangleF crystal = this.PlanRect(hx, hy);
            float from = box ? crystal.Top : this.planCenter.Y;
            this.Dim(g, DimSide.Top, 0, crystal.Left, crystal.Right, from, from, 2.0 * hx, keyX, RankMain);
            if (box)
            {
                this.Dim(g, DimSide.Left, 0, crystal.Top, crystal.Bottom, crystal.Left, crystal.Left, 2.0 * hy, keyY, RankMain);
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

        // ------------------------------------------------------------------
        // Простановка размеров по ГОСТ 2.307-2011 (`AMBER307`). Amber 06.11.2026, дословно: «Подписи разнести по ГОСТ-у».
        //
        // Пункты стандарта, сверены по его тексту:
        //   5.9   размерные линии предпочтительно выносить за контур: ряды стоят вне контура вида;
        //   5.10  выносные выходят за стрелки на 1–5 мм: `DimOverPx`;
        //   5.11  параллельные размерные не ближе 7 мм, от контура не ближе 10 мм: `DimPitchPx`, `DimFirstPx`;
        //   5.12  избегать пересечения размерных и выносных: меньший размер ближе к контуру, больший дальше,
        //         тогда выносные большего обходят меньший снаружи;
        //   5.21  нет места для стрелок: линия продолжается за выносные, стрелки снаружи;
        //   5.22  в цепочке при тесноте стрелки заменяют засечками под 45° или точками;
        //   5.24  число над размерной линией, ближе к середине; у вертикальной — НЕ поворотом, см. ниже (`AMBER324`);
        //   5.27, 5.30  нет места над линией: на продолжении линии или на полке линии-выноски;
        //   5.31  число не пересекают линии чертежа, контур и штриховка: число не садится ни на тело, ни на линию;
        //   5.38  перед диаметром знак диаметра.
        //
        // (`AMBER324`) Все числа горизонтальны. Amber 08.11.2026, к снимку вкладки «Источник», дословно: «1.Почему цифры
        // повёрнуты? ГОСТ такое запрещает.» Решение вопросником того же дня, дословно: «Все числа горизонтально
        // (Рекомендую)». Пункт 5.24 ставит число вертикального размера повёрнутым вдоль линии; горизонтальное число
        // стандарт допускает на полке линии-выноски (п. 5.27, 5.30). Поэтому у вертикальной линии число стоит
        // неповёрнутым РЯДОМ с ней (`BesidePx`), сперва снаружи от контура, потом со стороны контура, по оси линии
        // занимает свою высоту (`Along`); тесно — на продолжении линии рядом с ней, затем на полке выноски. Правило
        // п. 5.31 прежнее: место, где число садится на тело, на линию или на другое число, не годится (`Cost`, `Free`).
        // Поле вида сбоку — ширина числа, а не высота строки (`DimensionRoom`). То же в окне импорта GDML (`GdmlSketch`).
        //
        // Сторона размера стандартом не задана. Amber 06.11.2026, к снимку редактора, дословно: «А здесь почему не так
        // сделали? В ГОСТ иначе написано?» Не иначе: п. 5.8 допускает размерную прямо к линиям контура, п. 5.9 лишь
        // предпочитает вне контура. Поэтому тонкая стенка на краю тела стоит на уровне тела и короткой линией выходит
        // к ближайшей свободной стороне (`DimThrough`), а не длинными выносными вдоль всего чертежа.
        //
        // Шаг на экране. Чертёж не в масштабе листа, поэтому миллиметры ГОСТ переведены через цифру: при шрифте
        // редактора строка числа 14 точек, зазор 4, ряд 18 точек соответствует 7 мм, значит 1 мм = 2.57 точки.
        // Отсюда от контура до первого ряда 10 мм = 26 точек и вылет выносной 3 точки (1.2 мм из 1–5).
        // Шаг постоянен при любом окне: не влезает, значит скрывается вторичный размер, а не сжимаются ряды.
        // ------------------------------------------------------------------

        /// <summary>Выносные тоньше контура и полупрозрачны, как оси и засечки окна GDML: две рядом не сливаются во фланец.</summary>
        internal static readonly Color DimGuideInk = Color.FromArgb(0xA0, 0x20, 0x20, 0x20);
        internal const float DimGuideWidth = 0.7f;

        /// <summary>Шаг параллельных рядов, точек: ГОСТ 2.307-2011 п. 5.11, 7 мм.</summary>
        public const float DimPitchPx = 18f;

        /// <summary>От контура до первого ряда, точек: п. 5.11, 10 мм.</summary>
        public const float DimFirstPx = 26f;

        /// <summary>Вылет выносной за размерную, точек: п. 5.10.</summary>
        public const float DimOverPx = 3f;

        /// <summary>Стрелки снаружи: продолжение линии за выносную, точек: п. 5.21.</summary>
        const float DimTailPx = 8f;

        /// <summary>Наклонная часть линии-выноски, точек.</summary>
        const float ShelfRunPx = 10f;

        /// <summary>(`AMBER324`) Зазор между вертикальной размерной линией и горизонтальным числом рядом с ней, точек: дальше вылета выносной.</summary>
        const float BesidePx = 4f;

        /// <summary>Тело мельче этого: вторичные размеры уступают ему место.</summary>
        const double MinBodyPx = 40.0;

        /// <summary>В защите рядов больше, и ей нужно тело крупнее: иначе ряды не влезают и главные размеры теряются.</summary>
        const double MinBodyInShieldPx = 56.0;

        /// <summary>Сторона вида, на которой стоит размер.</summary>
        public enum DimSide
        {
            Left,
            Right,
            Top,
            Bottom,
        }

        /// <summary>Приоритеты размеров: скрываются с большего.</summary>
        const int RankMain = 1, RankSecond = 2, RankThin = 3, RankFocusOnly = 9;

        /// <summary>Что за линия: читает проба.</summary>
        public enum DimLineKind
        {
            Dimension,
            Extension,
            Leader,
        }

        /// <summary>Линия простановки последней отрисовки, в точках чертежа.</summary>
        public struct DimLine
        {
            public PointF A, B;
            public DimLineKind Kind;
            public DimSide Side;
            public bool OnTopView;
            public int Row;
            public int Group;
            public string Key;

            /// <summary>Размер без хвостов и продолжения, по оси ряда.</summary>
            public float SpanA, SpanB;
        }

        /// <summary>Окончание размерной линии: стрелка (острие, два пера) или засечка (два конца), как нарисовано.</summary>
        public struct DimEnd
        {
            public PointF[] Points;
            public string Key, Text;
            public float SpanA, SpanB;
            public bool InChain, OnTopView;
        }

        /// <summary>Один размер: отрезок вдоль оси ряда и точки на детали, откуда идут выносные.</summary>
        sealed class DimSpan
        {
            public float A, B, FromA, FromB;
            public string Text, Key;
            public int Rank;
            public SizeF Size;
            public bool Hidden;
            public float ExtendTo = float.NaN;
        }

        /// <summary>Группа одного ряда: одиночный размер или цепочка, стоящая на одной линии.</summary>
        sealed class DimGroup
        {
            public bool Plan;
            public DimSide Side;
            public int Chain;
            public int Row;
            public float Line;

            /// <summary>Стоит на уровне тела: линия горизонтальна, выносных нет, числа на стороне `Side`.</summary>
            public bool Through;
            public readonly List<DimSpan> Spans = new List<DimSpan>();
        }

        readonly List<DimGroup> dimGroups = new List<DimGroup>();
        readonly List<DimLine> dimLines = new List<DimLine>();
        readonly List<string> dimHidden = new List<string>();
        readonly List<DimEnd> dimEnds = new List<DimEnd>();
        readonly List<KeyValuePair<string, RectangleF>> dimNumbers = new List<KeyValuePair<string, RectangleF>>();
        int dimLevel = 3;
        bool dimPlan;
        bool dimWithFocus;
        double dimOuterHalf;
        RectangleF sideContour, planContour, costField;

        /// <summary>Линии простановки последней отрисовки. Читает проба.</summary>
        public DimLine[] DimensionLines
        {
            get { return this.dimLines.ToArray(); }
        }

        /// <summary>Окончания размерных линий последней отрисовки. Читает проба.</summary>
        public DimEnd[] DimensionEnds
        {
            get { return this.dimEnds.ToArray(); }
        }

        /// <summary>Размерные числа последней отрисовки и где они записаны. Читает проба.</summary>
        public KeyValuePair<string, RectangleF>[] DimensionNumbers
        {
            get { return this.dimNumbers.ToArray(); }
        }

        /// <summary>Цвет размерных чисел. Проба ставит свой, чтобы отличить их на картинке от всего остального.</summary>
        public Color DimensionNumberColor { get; set; } = Ink;

        /// <summary>Размеры, которым не нашлось свободного места. Читает проба.</summary>
        public string[] HiddenDimensions
        {
            get { return this.dimHidden.ToArray(); }
        }

        /// <summary>Контур тела на разрезе и на плане, точек чертежа. Читает проба.</summary>
        public RectangleF SideContour
        {
            get { return this.sideContour; }
        }

        public RectangleF TopContour
        {
            get { return this.planContour; }
        }

        /// <summary>Какие строки приоритета стоят сейчас: 3 — все, 1 — только главные. Читает проба.</summary>
        public int DimensionLevel
        {
            get { return this.dimLevel; }
        }

        bool DimVisible(DimSpan span)
        {
            return !span.Hidden && (span.Rank <= this.dimLevel || (this.dimWithFocus && this.Lit(span.Key)));
        }

        static bool Horizontal(DimSide side)
        {
            return side == DimSide.Top || side == DimSide.Bottom;
        }

        static bool Horizontal(DimGroup group)
        {
            return group.Through || Horizontal(group.Side);
        }

        /// <summary>Встать в очередь: a, b вдоль оси ряда, fromA, fromB поперёк, откуда начать выносные. Точки.</summary>
        void Dim(Graphics g, DimSide side, int chain, float a, float b, float fromA, float fromB, double value, string key, int rank, string prefix = null)
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
                t = fromA;
                fromA = fromB;
                fromB = t;
            }

            string text = (prefix ?? (key != null && key.EndsWith("Diameter", StringComparison.Ordinal) ? "⌀" : "")) + Format(value);
            DimSpan span = new DimSpan
            {
                A = a, B = b, FromA = fromA, FromB = fromB, Text = text, Key = key, Rank = rank,
                Size = g.MeasureString(text, this.Font),
            };
            DimGroup group = chain > 0 ? this.dimGroups.Find(x => x.Plan == this.dimPlan && x.Side == side && x.Chain == chain) : null;
            if (group == null)
            {
                group = new DimGroup { Plan = this.dimPlan, Side = side, Chain = chain };
                this.dimGroups.Add(group);
            }

            group.Spans.Add(span);
        }

        /// <summary>Тонкая стенка на краю тела: горизонтальная линия на уровне y, числа короткой выноской на свободной стороне.</summary>
        void DimThrough(Graphics g, DimSide side, int chain, float a, float b, float y, double value, string key, int rank)
        {
            int before = this.dimGroups.Count;
            this.Dim(g, side, chain + 100, a, b, y, y, value, key, rank);
            if (this.dimGroups.Count > before)
            {
                DimGroup group = this.dimGroups[before];
                group.Through = true;
                group.Row = -1;
                group.Line = y;
            }
        }

        /// <summary>Горизонтальный размер в мировых координатах; выносные от высот z1, z2.</summary>
        void DimX(Graphics g, DimSide side, int chain, double x1, double x2, double z1, double z2, double value, string key, int rank)
        {
            this.Dim(g, side, chain, this.X(x1), this.X(x2), this.Y(z1), this.Y(z2), value, key, rank);
        }

        /// <summary>Вертикальный размер в мировых координатах; выносные от абсцисс x1, x2.</summary>
        void DimZ(Graphics g, DimSide side, int chain, double z1, double z2, double x1, double x2, double value, string key, int rank)
        {
            this.Dim(g, side, chain, this.Y(z1), this.Y(z2), this.X(x1), this.X(x2), value, key, rank);
        }

        /// <summary>
        /// Число помещается между выносными. (`AMBER324`) Число всегда горизонтально, поэтому вдоль вертикальной
        /// линии оно занимает свою высоту, а не ширину: <see cref="Along"/>.
        /// </summary>
        static bool Roomy(DimSpan span, bool horizontal)
        {
            return Along(span, horizontal) + 8f <= span.B - span.A;
        }

        /// <summary>(`AMBER324`) Сколько неповёрнутое число занимает вдоль размерной линии: у горизонтальной — ширину, у вертикальной — высоту.</summary>
        static float Along(DimSpan span, bool horizontal)
        {
            return horizontal ? span.Size.Width : span.Size.Height;
        }

        static void VisibleBounds(DimGroup group, Func<DimSpan, bool> visible, out float lo, out float hi)
        {
            lo = float.MaxValue;
            hi = float.MinValue;
            foreach (DimSpan span in group.Spans)
            {
                if (visible(span))
                {
                    lo = Math.Min(lo, span.A);
                    hi = Math.Max(hi, span.B);
                }
            }
        }

        /// <summary>Цепочка — от двух видимых звеньев: одно оставшееся стоит как обычный размер.</summary>
        bool IsChain(DimGroup group)
        {
            // По приоритету и фокусу, без учёта скрытых местом: цепочка, потерявшая звено на расстановке, хвостов не отращивает.
            return group.Through || group.Spans.Count(x => x.Rank <= this.dimLevel || (this.dimWithFocus && this.Lit(x.Key))) > 1;
        }

        /// <summary>Сколько занимает группа вдоль ряда, с продолжением линии и числом на нём.</summary>
        void Extent(DimGroup group, out float lo, out float hi)
        {
            VisibleBounds(group, this.DimVisible, out lo, out hi);
            if (!this.IsChain(group))
            {
                DimSpan span = group.Spans.Find(x => this.DimVisible(x));
                if (!Roomy(span, Horizontal(group)))
                {
                    float tail = span.B - span.A >= ArrowRoom ? 0f : DimTailPx;
                    lo -= tail;
                    hi += tail + Reach + Along(span, Horizontal(group));
                }
            }
        }

        /// <summary>
        /// Ряды одной стороны одного вида. Меньшие размеры ближе к контуру, большие дальше (п. 5.12): группа встаёт
        /// за всеми, с кем у неё общий участок оси, поэтому её выносные обходят ближние ряды снаружи.
        /// </summary>
        List<DimGroup> AssignRows(bool plan, DimSide side)
        {
            List<DimGroup> groups = new List<DimGroup>();
            foreach (DimGroup group in this.dimGroups)
            {
                if (group.Plan == plan && group.Side == side && !group.Through && group.Spans.Exists(this.DimVisible))
                {
                    groups.Add(group);
                }
            }

            // (`AMBER324`) Ширина — как линия НАРИСОВАНА: у одиночного размера короче `ArrowRoom` стрелки снаружи и линия
            // длиннее размера на два хвоста. Иначе «10» чашки Петри (⌀90×10, слой пробы внутри сосуда 12) вставало ближе
            // «12», и его хвосты пересекали выносные «12» в 1.7 точки от своих концов (п. 5.12); было и до `AMBER324`.
            Func<DimGroup, float> width = x =>
            {
                float lo, hi;
                VisibleBounds(x, this.DimVisible, out lo, out hi);
                bool tails = !this.IsChain(x) && hi - lo < ArrowRoom;
                return hi - lo + (tails ? 2f * DimTailPx : 0f);
            };
            groups.Sort((x, y) => width(x).CompareTo(width(y)));
            for (int i = 0; i < groups.Count; i++)
            {
                float lo, hi;
                this.Extent(groups[i], out lo, out hi);
                int row = 0;
                for (int j = 0; j < i; j++)
                {
                    float lo2, hi2;
                    this.Extent(groups[j], out lo2, out hi2);
                    if (lo < hi2 + 6f && lo2 < hi + 6f)
                    {
                        row = Math.Max(row, groups[j].Row + 1);
                    }
                }

                groups[i].Row = row;
            }

            RectangleF contour = plan ? this.planContour : this.sideContour;
            float[] offsets = this.RowOffsets(groups, side);
            foreach (DimGroup group in groups)
            {
                float off = offsets[group.Row];
                group.Line = side == DimSide.Top ? contour.Top - off
                           : side == DimSide.Bottom ? contour.Bottom + off
                           : side == DimSide.Left ? contour.Left - off
                           : contour.Right + off;
            }

            return groups;
        }

        /// <summary>
        /// Отступ каждого ряда от контура, точек; последний элемент — где встал бы следующий ряд. Сверху и снизу шаг
        /// постоянный, `DimPitchPx` (п. 5.11). (`AMBER324`) Сбоку ряды разнесены по ширине числа: решение Amber 09.11.2026
        /// вопросником, дословно: «Разнести ряды (Рекомендую)». Горизонтальное число ряда стоит снаружи рядом со своей
        /// линией (`BesidePx`), и до линии следующего ряда остаётся `RowGapPx`: число каждого ряда встаёт у своей линии,
        /// без полок и без пересечений. Шаг не меньше `DimPitchPx`: п. 5.11 — минимум, а не точное число.
        /// </summary>
        float[] RowOffsets(List<DimGroup> groups, DimSide side)
        {
            int top = groups.Count > 0 ? groups.Max(x => x.Row) : -1;
            float[] offsets = new float[top + 2];
            offsets[0] = DimFirstPx;
            for (int row = 0; row <= top; row++)
            {
                float pitch = DimPitchPx;
                if (!Horizontal(side))
                {
                    foreach (DimGroup group in groups)
                    {
                        if (group.Row == row && !this.compactRows)
                        {
                            foreach (DimSpan span in group.Spans)
                            {
                                if (this.DimVisible(span))
                                {
                                    pitch = Math.Max(pitch, BesidePx + span.Size.Width + RowGapPx);
                                }
                            }
                        }
                    }
                }

                offsets[row + 1] = offsets[row] + pitch;
            }

            return offsets;
        }

        /// <summary>(`AMBER324`) Зазор от числа ряда сбоку до линии следующего ряда, точек.</summary>
        const float RowGapPx = 4f;

        /// <summary>(`AMBER324`) Второй проход раскладки: ряды сбоку прежним шагом, число внутреннего ряда — на полке.</summary>
        bool compactRows;

        /// <summary>Поля вида по его рядам: слева, справа, сверху, снизу. Без учёта фокуса: иначе чертёж прыгал бы при каждом переходе по полям.</summary>
        float[] DimensionRoom(Graphics g, bool plan)
        {
            this.dimWithFocus = false;
            float[] room = new float[4];
            float textH = g.MeasureString("0", this.Font).Height;
            foreach (DimSide side in new[] { DimSide.Left, DimSide.Right, DimSide.Top, DimSide.Bottom })
            {
                float need = 6f;
                int lastRow = -1;
                List<DimGroup> rows = this.AssignRows(plan, side);
                int topRow = rows.Count > 0 ? rows.Max(x => x.Row) : 0;
                float[] offsets = this.RowOffsets(rows, side);
                foreach (DimGroup group in rows)
                {
                    float off = offsets[group.Row];
                    lastRow = Math.Max(lastRow, group.Row);
                    int shelves = 0;
                    float shelfW = 0f;
                    if (this.IsChain(group))
                    {
                        float lo2, hi2;
                        VisibleBounds(group, this.DimVisible, out lo2, out hi2);
                        foreach (DimSpan span in group.Spans)
                        {
                            bool atEnd = span.A <= lo2 + 0.01f || span.B >= hi2 - 0.01f;
                            if (this.DimVisible(span) && !Roomy(span, Horizontal(side)) && (!atEnd || !Horizontal(side)))
                            {
                                shelves++;
                                shelfW = Math.Max(shelfW, span.Size.Width);
                            }
                        }
                    }

                    // (`AMBER324`) Число у вертикальной линии горизонтально и стоит снаружи рядом с ней: поле — его ширина.
                    float across = 0f;
                    foreach (DimSpan span in group.Spans)
                    {
                        if (this.DimVisible(span))
                        {
                            across = Math.Max(across, BesidePx + span.Size.Width + 1f);
                        }
                    }

                    float beyond;
                    switch (side)
                    {
                        case DimSide.Top:
                            beyond = shelves * DimPitchPx + textH + 1f;
                            break;
                        case DimSide.Bottom:
                            beyond = shelves * DimPitchPx + DimOverPx;
                            break;
                        default:
                            beyond = Math.Max(across, shelves > 0 ? ShelfRunPx + shelfW + 6f : 0f);
                            break;
                    }

                    need = Math.Max(need, off + beyond);
                    if (shelves > 0 && !Horizontal(side))
                    {
                        need = Math.Max(need, offsets[topRow] + beyond);
                    }

                    // (`AMBER324`) Ряды сбоку прежним шагом (второй проход): горизонтальное число внутреннего ряда, перекрытого
                    // внешним, рядом с линией не встаёт — ему поле под полку за всеми рядами, иначе главный размер скрывался.
                    // (`AMBER324`) Второй проход: тело мелкое, и главный горизонтальный размер может не поместиться ни над
                    // линией, ни на её продолжении (маринелли в защите 280×427: «⌀130» внутри «⌀200»). Ему — ряд под полку за
                    // всеми рядами: вертикального места в окне обычно вдоволь, а скрытый главный хуже полки.
                    if (this.compactRows && Horizontal(side) && !this.IsChain(group)
                        && group.Spans.Exists(x => this.DimVisible(x) && x.Rank == RankMain && !Roomy(x, true)))
                    {
                        need = Math.Max(need, offsets[topRow] + DimPitchPx + (side == DimSide.Top ? textH + 1f : DimOverPx));
                    }

                    if (this.compactRows && !Horizontal(side) && group.Row < topRow && !(group.Row == 0 && across + 2f * Gap <= DimFirstPx))
                    {
                        float lo, hi;
                        VisibleBounds(group, this.DimVisible, out lo, out hi);
                        bool covered = rows.Exists(other =>
                        {
                            float lo2, hi2;
                            VisibleBounds(other, this.DimVisible, out lo2, out hi2);
                            return other.Row > group.Row && lo2 < hi && lo < hi2;
                        });
                        if (covered)
                        {
                            need = Math.Max(need, offsets[topRow] + ShelfRunPx + across + 6f);
                        }
                    }
                }

                foreach (DimGroup group in this.dimGroups)
                {
                    if (group.Plan == plan && group.Side == side && group.Through)
                    {
                        foreach (DimSpan span in group.Spans)
                        {
                            if (this.DimVisible(span))
                            {
                                need = Math.Max(need, ShelfRunPx + span.Size.Width + 6f);
                            }
                        }
                    }
                }

                // Запасной ряд под размер, который покажет только фокус: чертёж не сдвигается при переходе по полям.
                // (`AMBER324`) Сбоку число горизонтально: запас — его ширина, а не высота строки.
                foreach (DimGroup group in this.dimGroups)
                {
                    if (group.Plan == plan && group.Side == side && !group.Through && group.Spans.Exists(x => !this.DimVisible(x)))
                    {
                        float spare = Horizontal(side) ? textH + 1f
                                    : group.Spans.Where(x => !this.DimVisible(x)).Max(x => BesidePx + x.Size.Width + 1f);
                        need = Math.Max(need, offsets[lastRow + 1] + spare);
                        break;
                    }
                }

                // Запас на рамку вида и на округление координат тела до целых точек.
                room[(int)side] = need + 3f;
            }

            // Веер цепочки на уровне тела идёт вниз: нижнему полю хватает места на все её полки.
            RectangleF body = plan ? this.planContour : this.sideContour;
            foreach (DimGroup group in this.dimGroups)
            {
                if (group.Plan == plan && group.Through)
                {
                    int shown = group.Spans.Count(this.DimVisible);
                    room[(int)DimSide.Bottom] = Math.Max(room[(int)DimSide.Bottom], group.Line + (shown + 1) * DimPitchPx + 3f - body.Bottom);
                }
            }

            return room;
        }

        /// <summary>Пересекает ли отрезок прямоугольник.</summary>
        static bool Hits(PointF a, PointF b, RectangleF r)
        {
            // Алгоритм Лианга — Барски: отрезок против четырёх полуплоскостей.
            float t0 = 0f, t1 = 1f, dx = b.X - a.X, dy = b.Y - a.Y;
            float[] p = { -dx, dx, -dy, dy };
            float[] q = { a.X - r.Left, r.Right - a.X, a.Y - r.Top, r.Bottom - a.Y };
            for (int i = 0; i < 4; i++)
            {
                if (p[i] == 0f)
                {
                    if (q[i] < 0f)
                    {
                        return false;
                    }

                    continue;
                }

                float t = q[i] / p[i];
                if (p[i] < 0f)
                {
                    t0 = Math.Max(t0, t);
                }
                else
                {
                    t1 = Math.Min(t1, t);
                }
            }

            return t0 <= t1;
        }

        static bool Cross(PointF a, PointF b, PointF c, PointF d)
        {
            Func<PointF, PointF, PointF, float> side = (p, q, r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            float d1 = side(a, b, c), d2 = side(a, b, d), d3 = side(c, d, a), d4 = side(c, d, b);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        /// <summary>Линии группы: размерная и выносные. Хвосты и продолжения стоят на той же прямой.</summary>
        void AddGroupLines(DimGroup group, int id)
        {
            bool h = Horizontal(group);
            float dir = group.Side == DimSide.Top || group.Side == DimSide.Left ? -1f : 1f;
            float spanA = 0f, spanB = 0f;
            Action<DimLineKind, float, float, float, float, string> add = (kind, a1, c1, a2, c2, key) =>
            {
                this.dimLines.Add(new DimLine
                {
                    A = h ? new PointF(a1, c1) : new PointF(c1, a1),
                    B = h ? new PointF(a2, c2) : new PointF(c2, a2),
                    Kind = kind, Side = group.Side, OnTopView = group.Plan, Row = group.Row, Group = id, Key = key,
                    SpanA = spanA, SpanB = spanB,
                });
            };
            foreach (DimSpan span in group.Spans)
            {
                if (!this.DimVisible(span))
                {
                    continue;
                }

                spanA = span.A;
                spanB = span.B;
                float lo = span.A, hi = span.B;
                if (!this.IsChain(group) && span.B - span.A < ArrowRoom)
                {
                    lo -= DimTailPx;
                    hi += DimTailPx;
                }

                if (!float.IsNaN(span.ExtendTo))
                {
                    lo = Math.Min(lo, span.ExtendTo);
                    hi = Math.Max(hi, span.ExtendTo);
                }

                add(DimLineKind.Dimension, lo, group.Line, hi, group.Line, span.Key);
                if (group.Through)
                {
                    continue;
                }

                add(DimLineKind.Extension, span.A, span.FromA, span.A, group.Line + dir * DimOverPx, span.Key);
                add(DimLineKind.Extension, span.B, span.FromB, span.B, group.Line + dir * DimOverPx, span.Key);
            }
        }

        /// <summary>Встала ли подпись свободно: поле вида, подписи, тело, линии.</summary>
        bool Free(Spot spot, int ownGroup, bool allowLeaderCross)
        {
            if (this.Cost(spot.Box) > 0f)
            {
                return false;
            }

            RectangleF field = !this.costField.IsEmpty ? this.costField : new RectangleF(1f, 1f, this.Width - 2f, this.Height - 2f);
            if (spot.Extension)
            {
                // Продолжение линии — та же размерная: чужие выносные оно не пересекает (п. 5.12).
                // (`AMBER324`) Число стоит рядом с линией, а не на ней: продолжение идёт по самой размерной.
                float c = spot.LineAt;
                PointF a = spot.Vertical ? new PointF(c, spot.ExtendFrom) : new PointF(spot.ExtendFrom, c);
                PointF b = spot.Vertical ? new PointF(c, spot.ExtendTo) : new PointF(spot.ExtendTo, c);
                foreach (DimLine line in this.dimLines)
                {
                    if (line.Group != ownGroup && line.Kind == DimLineKind.Extension && Cross(a, b, line.A, line.B))
                    {
                        return false;
                    }
                }

                // И через уже стоящее число оно не идёт.
                foreach (SketchLabel label in this.labels)
                {
                    if (Hits(a, b, label.Bounds))
                    {
                        return false;
                    }
                }
            }

            PointF[] leader = spot.Leader;
            if (leader == null)
            {
                return true;
            }

            for (int i = 0; i + 1 < leader.Length; i++)
            {
                if (!field.Contains(leader[i + 1]))
                {
                    return false;
                }

                foreach (DimLine line in this.dimLines)
                {
                    // Выноска начинается на своей размерной и у самого начала встречает вылет своей выносной: так устроена цепочка.
                    if (line.Group == ownGroup && line.Kind != DimLineKind.Leader)
                    {
                        continue;
                    }

                    if (allowLeaderCross && i == 0 && line.Kind == DimLineKind.Dimension)
                    {
                        continue;
                    }

                    if (Cross(leader[i], leader[i + 1], line.A, line.B))
                    {
                        return false;
                    }

                    // Полка по чужой размерной: лежат на одной прямой, Cross этого не видит.
                    bool flat = Math.Abs(leader[i].Y - leader[i + 1].Y) < 0.5f && Math.Abs(line.A.Y - line.B.Y) < 0.5f && Math.Abs(leader[i].Y - line.A.Y) < 2f;
                    if (flat && Math.Min(leader[i].X, leader[i + 1].X) < Math.Max(line.A.X, line.B.X) && Math.Min(line.A.X, line.B.X) < Math.Max(leader[i].X, leader[i + 1].X))
                    {
                        return false;
                    }
                }

                foreach (SketchLabel label in this.labels)
                {
                    if (Hits(leader[i], leader[i + 1], label.Bounds))
                    {
                        return false;
                    }
                }

                foreach (RectangleF band in this.obstacles)
                {
                    if (i > 0 && Hits(leader[i], leader[i + 1], band))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>Поставить все размеры вида: ряды, линии, числа. Вторичный без свободного места скрывается, наложения нет.</summary>
        void FlushDimensions(Graphics g, bool plan)
        {
            this.dimPlan = plan;
            this.dimWithFocus = true;
            RectangleF contour = plan ? this.planContour : this.sideContour;
            Rectangle panel = plan ? this.planPanel : this.sidePanel;
            this.costField = panel.IsEmpty ? new RectangleF(1f, 1f, this.Width - 2f, this.Height - 2f)
                                           : new RectangleF(panel.X + 1f, panel.Y + 1f, panel.Width - 2f, panel.Height - 2f);
            this.obstacles.Add(contour);
            List<DimGroup> groups = new List<DimGroup>();
            foreach (DimSide side in new[] { DimSide.Top, DimSide.Bottom, DimSide.Left, DimSide.Right })
            {
                groups.AddRange(this.AssignRows(plan, side));
            }

            groups.AddRange(this.dimGroups.Where(x => x.Plan == plan && x.Through && x.Spans.Exists(this.DimVisible)));
            int first = this.dimLines.Count;
            for (int i = 0; i < groups.Count; i++)
            {
                this.AddGroupLines(groups[i], first + i);
            }

            // Числа: главные первыми, чтобы вторичные уступали им, а не наоборот.
            List<KeyValuePair<DimGroup, DimSpan>> order = new List<KeyValuePair<DimGroup, DimSpan>>();
            foreach (DimGroup group in groups)
            {
                foreach (DimSpan span in group.Spans)
                {
                    if (this.DimVisible(span))
                    {
                        order.Add(new KeyValuePair<DimGroup, DimSpan>(group, span));
                    }
                }
            }

            List<KeyValuePair<DimSpan, Spot>> placed = new List<KeyValuePair<DimSpan, Spot>>();
            Dictionary<DimGroup, int> shelfCount = new Dictionary<DimGroup, int>();
            // Сперва числа рядов: у них меньше выбора. Цепочки на уровне тела потом: их веер встанет и сверху, и снизу.
            // Полки веером: крайний к своей стороне стоит ближе всех.
            foreach (KeyValuePair<DimGroup, DimSpan> item in order.OrderBy(x => this.Lit(x.Value.Key) ? 0 : 1).ThenBy(x => x.Key.Through ? 1 : 0).ThenBy(x => Roomy(x.Value, Horizontal(x.Key)) ? 0 : 1).ThenBy(x => x.Value.Rank).ThenBy(x => ShelfOrder(x.Key, x.Value, contour)))
            {
                DimGroup group = item.Key;
                DimSpan span = item.Value;
                bool lit = this.Lit(span.Key);
                int k;
                shelfCount.TryGetValue(group, out k);
                bool found = false;
                Spot chosen = new Spot();
                // Сперва без пересечений; размерную линию дальнего ряда выноска пересекает, только когда иначе негде.
                foreach (bool allowCross in new[] { false, true })
                {
                    foreach (Spot spot in this.NumberSpots(group, span, k, contour))
                    {
                        if (!found && this.Free(spot, first + groups.IndexOf(group), allowCross))
                        {
                            chosen = spot;
                            found = true;
                        }
                    }
                }

                // Поле в фокусе размер показывает всегда: человек смотрит именно на него. Пусть и поверх соседа, пока фокус тут.
                if (!found && lit)
                {
                    foreach (Spot spot in this.NumberSpots(group, span, k, contour))
                    {
                        if (!found && this.costField.Contains(spot.Box))
                        {
                            chosen = spot;
                            found = true;
                        }
                    }

                    if (!found)
                    {
                        // Ни одно место не в поле: у самой линии, прижато к рамке вида.
                        float mid = 0.5f * (span.A + span.B);
                        bool across = Horizontal(group);
                        float x = across ? mid - 0.5f * span.Size.Width : group.Line + 3f;
                        float y = across ? group.Line - span.Size.Height - 1f : mid - 0.5f * span.Size.Height;
                        x = Math.Max(this.costField.Left, Math.Min(x, this.costField.Right - span.Size.Width));
                        y = Math.Max(this.costField.Top, Math.Min(y, this.costField.Bottom - span.Size.Height));
                        chosen = new Spot { Box = new RectangleF(x, y, span.Size.Width, span.Size.Height) };
                        found = true;
                    }
                }

                if (!found)
                {
                    // Наложения не допускаем: размер скрывается, след остаётся в счётчике и в журнале.
                    span.Hidden = true;
                    this.crowded++;
                    this.dimHidden.Add(span.Text + " (" + span.Key + ", ранг " + span.Rank + ")");
                    continue;
                }

                if (chosen.Leader != null)
                {
                    shelfCount[group] = k + 1;
                    for (int i = 0; i + 1 < chosen.Leader.Length; i++)
                    {
                        this.dimLines.Add(new DimLine
                        {
                            A = chosen.Leader[i], B = chosen.Leader[i + 1], Kind = DimLineKind.Leader, Side = group.Side,
                            OnTopView = plan, Row = group.Row, Group = first + groups.IndexOf(group), Key = span.Key,
                        });
                    }
                }

                if (chosen.Extension)
                {
                    span.ExtendTo = chosen.ExtendTo;
                    bool h = Horizontal(group);
                    this.dimLines.Add(new DimLine
                    {
                        A = h ? new PointF(chosen.ExtendFrom, group.Line) : new PointF(group.Line, chosen.ExtendFrom),
                        B = h ? new PointF(chosen.ExtendTo, group.Line) : new PointF(group.Line, chosen.ExtendTo),
                        Kind = DimLineKind.Dimension, Side = group.Side, OnTopView = plan, Row = group.Row, Group = first + groups.IndexOf(group), Key = span.Key,
                    });
                }

                // Сразу в `labels`: следующее число его обходит. Печать ниже, поверх всех линий.
                this.labels.Add(new SketchLabel { Bounds = chosen.Box, Text = span.Text, Key = span.Key });
                placed.Add(new KeyValuePair<DimSpan, Spot>(span, chosen));
            }

            // Скрытым линии не положены: собрать заново. Выноски и продолжения остаются за теми, кто встал.
            this.dimLines.RemoveAll(line => line.OnTopView == plan && line.Kind == DimLineKind.Extension);
            for (int i = this.dimLines.Count - 1; i >= first; i--)
            {
                if (this.dimLines[i].Kind == DimLineKind.Dimension)
                {
                    this.dimLines.RemoveAt(i);
                }
            }

            for (int i = 0; i < groups.Count; i++)
            {
                this.AddGroupLines(groups[i], first + i);
            }

            this.DrawDimLines(g, plan, groups, placed);
        }

        /// <summary>Чтобы выноски цепочки не пересекались: к своей стороне сперва крайний.</summary>
        static float ShelfOrder(DimGroup group, DimSpan span, RectangleF contour)
        {
            float mid = 0.5f * (span.A + span.B);
            if (group.Through)
            {
                // Ближний к своей стороне идёт прямо; следующий ниже: выноски не пересекаются.
                return group.Side == DimSide.Left ? mid : -mid;
            }

            if (Horizontal(group.Side))
            {
                float center = 0.5f * (contour.Left + contour.Right);
                return mid < center ? mid - contour.Left : contour.Right - mid;
            }

            return mid;
        }

        /// <summary>Места для числа в порядке убывания желательности: над линией у середины, на продолжении, на полке.</summary>
        IEnumerable<Spot> NumberSpots(DimGroup group, DimSpan span, int shelf, RectangleF contour)
        {
            bool h = Horizontal(group);
            float w = span.Size.Width, t = span.Size.Height, mid = 0.5f * (span.A + span.B);
            float line = group.Line;
            if (group.Through)
            {
                // Короткая линия к свободной стороне: первая прямо по уровню, следующие веером вниз, потом вверх.
                float side = group.Side == DimSide.Left ? -1f : 1f;
                float edge = group.Side == DimSide.Left ? contour.Left : contour.Right;
                for (int extra = 0; extra < 12; extra++)
                {
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        if (shelf + extra == 0 && sign < 0f)
                        {
                            continue;
                        }

                        float y = line + sign * (shelf + extra) * DimPitchPx;
                        PointF elbow = new PointF(edge + side * ShelfRunPx, y);
                        yield return new Spot
                        {
                            Box = new RectangleF(side > 0f ? elbow.X + 3f : elbow.X - w - 3f, y - t - 1f, w, t),
                            Leader = new[] { new PointF(mid, line), elbow, new PointF(elbow.X + side * (w + 6f), y) },
                        };
                    }
                }

                yield break;
            }

            // (`AMBER324`) У вертикальной линии число НЕ повёрнуто: стоит рядом с ней, сперва снаружи от контура, потом
            // со стороны контура; по оси линии занимает свою высоту.
            float outward = group.Side == DimSide.Top || group.Side == DimSide.Left ? -1f : 1f;
            float along = h ? w : t;
            Func<float, float, RectangleF> beside = (lo, side) =>
                new RectangleF(side < 0f ? line - BesidePx - w : line + BesidePx, lo, w, t);
            if (Roomy(span, h))
            {
                // П. 5.24: над линией и как можно ближе к середине. У вертикальной — рядом с линией, число горизонтально.
                float room = 0.5f * (span.B - span.A - along) - 4f;
                foreach (float side in h ? new[] { 0f } : new[] { outward, -outward })
                {
                    foreach (float shift in new[] { 0f, 0.5f, -0.5f, 1f, -1f })
                    {
                        float at = mid + shift * room;
                        yield return h ? new Spot { Box = new RectangleF(at - w / 2f, line - t - 1f, w, t) }
                                       : new Spot { Box = beside(at - t / 2f, side), Vertical = true };
                    }
                }
            }
            else if (h && this.compactRows && span.Rank == RankMain && w <= span.B - span.A)
            {
                // (`AMBER324`) Второй проход, главный горизонтальный размер в мелком теле (маринелли в защите 280×427: «⌀130»
                // внутри «⌀200»): число входит между своими выносными без положенного запаса (рамка MeasureString шире чернил
                // на поля шрифта, так что чернила выносных не касаются) — ставится над линией у
                // середины. На продолжении оно легло бы на выносные соседнего ряда, а скрытый главный хуже тесного.
                // Поднято над линией на 3 точки: перья стрелок у выносных (0.6 S = 2.4 точки) не заходят в рамку числа.
                yield return new Spot { Box = new RectangleF(mid - w / 2f, line - t - 3f, w, t) };
            }
            else if (!h)
            {
                // (`AMBER324`) Короткий вертикальный размер: горизонтальное число стоит РЯДОМ с линией, не на ней, и
                // помещаться между выносными ему не нужно — свои выносные кончаются в `DimOverPx` от линии, ближе `BesidePx`.
                // Поэтому сперва у середины, как у длинного, и лишь потом на продолжении. Без этого «10» чашки Петри
                // (⌀90×10) уходило на продолжение, упиралось в выносные «12» внешнего ряда и скрывалось.
                foreach (float side in new[] { outward, -outward })
                {
                    yield return new Spot { Box = beside(mid - t / 2f, side), Vertical = true };
                }
            }

            if (Roomy(span, h))
            {
                // Места у середины уже предложены выше.
            }
            else if (this.IsChain(group))
            {
                // Крайнее звено цепочки: число на продолжении линии, за засечкой. Выноска поперёк дальнего ряда не нужна.
                float first = float.MaxValue, last = float.MinValue;
                foreach (DimSpan other in group.Spans)
                {
                    if (this.DimVisible(other))
                    {
                        first = Math.Min(first, other.A);
                        last = Math.Max(last, other.B);
                    }
                }

                foreach (float end in new[] { -1f, 1f })
                {
                    if (end < 0f ? span.A <= first + 0.01f : span.B >= last - 0.01f)
                    {
                        float from = end > 0f ? span.B : span.A;
                        float near = from + end * Reach, far = near + end * along;
                        float lo = Math.Min(near, far);
                        foreach (float side in h ? new[] { 0f } : new[] { outward, -outward })
                        {
                            yield return new Spot
                            {
                                Box = h ? new RectangleF(lo, line - t - 1f, w, t) : beside(lo, side),
                                Vertical = !h, Extension = true, ExtendFrom = from, ExtendTo = far, LineAt = line,
                            };
                        }
                    }
                }
            }
            else
            {
                // П. 5.21, 5.30: на продолжении размерной линии, за стрелкой, сперва наружу от контура.
                float tail = span.B - span.A >= ArrowRoom ? 0f : DimTailPx;
                float center = h ? 0.5f * (contour.Left + contour.Right) : 0.5f * (contour.Top + contour.Bottom);
                float[] ends = mid < center ? new[] { -1f, 1f } : new[] { 1f, -1f };
                foreach (float end in ends)
                {
                    float from = end > 0f ? span.B + tail : span.A - tail;
                    float near = from + end * Reach, far = near + end * along;
                    float lo = Math.Min(near, far);
                    foreach (float side in h ? new[] { 0f } : new[] { outward, -outward })
                    {
                        yield return new Spot
                        {
                            Box = h ? new RectangleF(lo, line - t - 1f, w, t) : beside(lo, side),
                            Vertical = !h, Extension = true, ExtendFrom = from, ExtendTo = far, LineAt = line,
                        };
                    }
                }
            }

            // П. 5.27, 5.30: на полке линии-выноски. Полки идут рядами с тем же шагом, число над полкой.
            float dir = group.Side == DimSide.Top || group.Side == DimSide.Left ? -1f : 1f;
            for (int extra = 0; extra < 12; extra++)
            {
                int k = shelf + extra;
                if (h)
                {
                    float center = 0.5f * (contour.Left + contour.Right);
                    foreach (float side in mid < center ? new[] { -1f, 1f } : new[] { 1f, -1f })
                    {
                        float y = line + dir * (k + 1) * DimPitchPx;
                        PointF elbow = new PointF(mid + side * ShelfRunPx, y);
                        PointF end = new PointF(elbow.X + side * (w + 6f), y);
                        yield return new Spot
                        {
                            Box = new RectangleF(side > 0f ? elbow.X + 3f : elbow.X - w - 3f, y - t - 1f, w, t),
                            Leader = new[] { new PointF(mid, line), elbow, end },
                        };
                    }
                }
                else
                {
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        float y = mid + sign * (k - 0.5f) * DimPitchPx;
                        float past = 0f;
                        foreach (DimGroup other in this.dimGroups)
                        {
                            if (other.Plan == group.Plan && other.Side == group.Side && !other.Through && other.Row > group.Row && other.Spans.Exists(this.DimVisible))
                            {
                                past = Math.Max(past, Math.Abs(other.Line - group.Line));
                            }
                        }

                        PointF elbow = new PointF(line + dir * (ShelfRunPx + past), y);
                        PointF end = new PointF(elbow.X + dir * (w + 6f), y);
                        yield return new Spot
                        {
                            Box = new RectangleF(dir > 0f ? elbow.X + 3f : elbow.X - w - 3f, y - t - 1f, w, t),
                            Leader = new[] { new PointF(line, mid), elbow, end },
                        };
                    }
                }
            }
        }

        void EndMark(PointF[] points, DimSpan span, bool plan, bool chain)
        {
            this.dimEnds.Add(new DimEnd { Points = points, Key = span.Key, Text = span.Text, SpanA = span.A, SpanB = span.B, InChain = chain, OnTopView = plan });
        }

        /// <summary>Нарисовать линии вида и окончания, затем числа поверх.</summary>
        void DrawDimLines(Graphics g, bool plan, List<DimGroup> groups, List<KeyValuePair<DimSpan, Spot>> placed)
        {
            using (Pen ext = new Pen(DimGuideInk, DimGuideWidth))
            using (Pen dim = new Pen(Ink, 1f))
            using (Pen extLit = new Pen(LitColor, 1.2f))
            using (Pen dimLit = new Pen(LitColor, 1.8f))
            using (Brush ink = new SolidBrush(this.DimensionNumberColor))
            using (Brush lit = new SolidBrush(this.DimensionNumberColor == Ink ? LitColor : this.DimensionNumberColor))
            {
                foreach (DimLine line in this.dimLines)
                {
                    if (line.OnTopView != plan)
                    {
                        continue;
                    }

                    bool on = this.Lit(line.Key);
                    Pen pen = line.Kind == DimLineKind.Extension ? (on ? extLit : ext) : (on ? dimLit : dim);

                    g.DrawLine(pen, line.A, line.B);
                }

                foreach (DimGroup group in groups)
                {
                    bool h = Horizontal(group);
                    foreach (DimSpan span in group.Spans)
                    {
                        if (!this.DimVisible(span))
                        {
                            continue;
                        }

                        Pen pen = this.Lit(span.Key) ? dimLit : dim;
                        bool roomy = span.B - span.A >= ArrowRoom;
                        if (this.IsChain(group))
                        {
                            // П. 5.22: в цепочке вместо стрелок у тесного звена засечки, и у соседа на общей границе тоже.
                            Func<float, bool> neighbourThin = at => group.Spans.Exists(o => o != span && this.DimVisible(o)
                                && o.B - o.A < ArrowRoom && (Math.Abs(o.A - at) < 0.5f || Math.Abs(o.B - at) < 0.5f));
                            if (roomy && !neighbourThin(span.A))
                            {
                                this.EndMark(Arrow(g, pen, h, span.A, group.Line, +1f), span, plan, true);
                            }
                            else
                            {
                                this.EndMark(Tick(g, pen, h, span.A, group.Line), span, plan, true);
                            }

                            if (roomy && !neighbourThin(span.B))
                            {
                                this.EndMark(Arrow(g, pen, h, span.B, group.Line, -1f), span, plan, true);
                            }
                            else
                            {
                                this.EndMark(Tick(g, pen, h, span.B, group.Line), span, plan, true);
                            }
                        }
                        else if (roomy)
                        {
                            this.EndMark(Arrow(g, pen, h, span.A, group.Line, +1f), span, plan, false);
                            this.EndMark(Arrow(g, pen, h, span.B, group.Line, -1f), span, plan, false);
                        }
                        else
                        {
                            // П. 5.21: стрелки снаружи, острием к выносным.
                            this.EndMark(Arrow(g, pen, h, span.A, group.Line, -1f), span, plan, false);
                            this.EndMark(Arrow(g, pen, h, span.B, group.Line, +1f), span, plan, false);
                        }
                    }
                }

                foreach (KeyValuePair<DimSpan, Spot> item in placed)
                {
                    if (item.Key.Hidden)
                    {
                        continue;
                    }

                    Brush brush = this.Lit(item.Key.Key) ? lit : ink;
                    RectangleF box = item.Value.Box;
                    this.dimNumbers.Add(new KeyValuePair<string, RectangleF>(item.Key.Text, box));
                    // (`AMBER324`) Без поворота: все числа горизонтальны.
                    g.DrawString(item.Key.Text, this.Font, brush, box.X, box.Y);
                }
            }
        }

        /// <summary>Все размеры обоих видов: очередь заново, координаты при нынешнем масштабе.</summary>
        void QueueDimensions(Graphics g, GeometryModel m, double halfWidth, double height,
                             double tfr, double tsr, double tfc, double tsc, double tm, double tfg, double tsg, double zFace)
        {
            this.dimGroups.Clear();
            this.dimPlan = false;
            if (this.Mode == SketchMode.Detector)
            {
                this.Annotate(g, m, halfWidth, height, tfr, tsr, tfc, tsc, tm, tfg, tsg);
            }
            else if (this.Mode == SketchMode.Source)
            {
                this.AnnotateSource(g, m, zFace);
                this.AnnotateShield(g, m);
            }

            if (this.planView)
            {
                this.dimPlan = true;
                this.QueuePlanDimensions(g, m);
                this.dimPlan = false;
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
            public bool Vertical, Extension;
            public float ExtendFrom, ExtendTo, LineAt;
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
                return;
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
            RectangleF field = !this.costField.IsEmpty ? this.costField : new RectangleF(1f, 1f, this.Width - 2f, this.Height - 2f);
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

            // П. 5.31: число не пересекают линии чертежа.
            foreach (DimLine line in this.dimLines)
            {
                if (Hits(line.A, line.B, box))
                {
                    cost += 50f;
                }
            }

            return cost;
        }

        /// <summary>Стрелка остриём в точку, оперением в сторону <paramref name="dir"/>.</summary>
        internal static PointF[] Arrow(Graphics g, Pen pen, bool horizontal, float at, float fixedCoord, float dir)
        {
            const float S = 4f;
            PointF tip = horizontal ? new PointF(at, fixedCoord) : new PointF(fixedCoord, at);
            PointF one = horizontal ? new PointF(at + dir * S, fixedCoord - S * 0.6f) : new PointF(fixedCoord - S * 0.6f, at + dir * S);
            PointF two = horizontal ? new PointF(at + dir * S, fixedCoord + S * 0.6f) : new PointF(fixedCoord + S * 0.6f, at + dir * S);
            g.DrawLine(pen, tip, one);
            g.DrawLine(pen, tip, two);
            return new[] { tip, one, two };
        }

        /// <summary>Засечка вместо стрелки — короткий наклонный штрих под 45° (п. 5.22).</summary>
        static PointF[] Tick(Graphics g, Pen pen, bool horizontal, float at, float fixedCoord)
        {
            const float S = 3f;
            PointF a = horizontal ? new PointF(at - S, fixedCoord + S) : new PointF(fixedCoord + S, at - S);
            PointF b = horizontal ? new PointF(at + S, fixedCoord - S) : new PointF(fixedCoord - S, at + S);
            g.DrawLine(pen, a, b);
            return new[] { a, b };
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
