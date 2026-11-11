using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace GeometryTopViewProbeA307
{
    /// <summary>
    /// ПРИЁМКА `AMBER307`: вид сверху и простановка размеров по ГОСТ 2.307-2011 на эскизах редактора геометрии.
    /// Решения Amber 06.11.2026: вид отдельным эскизом рядом с разрезом, как в окне импорта GDML; план только на
    /// вкладках «Детектор» и «Образец» («Источник»); «Подписи разнести по ГОСТ-у».
    ///
    ///     geometrytopviewprobea307 [--cover=&lt;файл&gt;]
    ///
    /// ⛔ ОКОН НЕТ: `OnPaint` зовётся отражением на `Graphics` от `Bitmap`.
    ///
    /// (0) Положительный контроль заслона: настоящая раскладка прошлой версии на RC-110 обязана дать отказ.
    /// (1) Покрытие: 5 кристаллов × 8 сцен источника × размеры поля: две рамки на «Детекторе» и «Источнике»,
    /// один разрез на «Защите». Заслон ГОСТ: подписи не накладываются и стоят в своей рамке; сквозь число не идёт
    /// линия; число не на теле; размерные и чужие выносные не пересекаются; ряды с постоянным шагом, вложенный
    /// размер ближе к контуру; выносная выходит за размерную на постоянный вылет.
    /// (2) Числа плана против `CrystalBoxInScene` и чисел, вписанных руками. (3) То же в точках: кристалл и корпус
    /// меряются по цвету. (4) Вид перестраивается при смене положения. (5) Подсветка там, где размер стоит.
    /// (6) Подпись разворота и глубина на RU и EN, в том числе на одном разрезе. (7) Источник в плане: пиксели.
    /// (8) Миниатюра прежняя. (9) Чертёж не меняет модель.
    /// (10) Сцена: случайная модель, на которой числа разреза без рамки вида уходили за неё.
    ///
    /// (11) `AMBER324`, решения Amber 08.11.2026 вопросником, дословно: «Все числа горизонтально (Рекомендую)» и
    /// «Со стороны пробы (Рекомендую)». Ни одного повёрнутого числа на всех чертежах покрытия (рамка выше ширины,
    /// у `Picture` — и по чернилам); вид со стороны пробы: проба и сосуд поверх прибора, контур прибора под ними
    /// штриховой, прибор вокруг малой банки виден, на «Детекторе» пробы нет; ось Y плана вниз (трапеция).
    /// Положительный контроль: эта же проба, собранная против сборки до `AMBER324`, обязана отказать
    /// (журнал `handover/handover-2026-11-08-a324.md`), и синтетика `TurnedControl`.
    ///
    /// (12) `AMBER325`, Amber 11.11.2026, дословно: «Запиши задачу, нужно переделать скетч на From detector side.»; решения
    /// вопросником того же дня, дословно: «Сделай в скетче CheckBox - с какой стороны показывать образец. С верху или с низу.
    /// Пускай пользователь решает.», «Со стороны детектора, помнить в настройках (Рекомендую)», «Штрихом, как в виде со стороны
    /// пробы (Рекомендую)», «Только «Источник» (Рекомендую)». На «Источнике» по умолчанию вид со стороны детектора: прибор
    /// поверх, закрытое им — штрихом (банка ⌀40 на AS 80x80: в центре кристалл, контур банки штриховой; трапеция — основание 2
    /// сверху; точка — штриховой кружок); галка «Со стороны пробы» у заголовка — вид (11); на «Детекторе» и «Защите» галки нет;
    /// выбор — в главном конфиге (`GeometrySketchPlanFromSample`): пишется галкой, читается при следующем открытии, старый
    /// конфиг без поля — со стороны детектора. Покрытие и подписи — в обоих положениях галки. Новые члены берутся отражением:
    /// проба собирается и против сборки до `AMBER325` — и обязана там отказать (журнал `handover/handover-2026-11-11-a325.md`).
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0; есть расхождение: код 1.
    /// Положительный контроль всей пробы: мутанты, журнал `handover/handover-2026-11-05-design-gdml.md`.
    /// </summary>
    static class Program
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Заливку по краям закрывает контур, с двух сторон и с округлением: до 3.5 точек меры.
        /// Переставленные толщины куба это 57 точек, развёрнутый брусок сотни.</summary>
        const double Pen = 3.5;

        static int checks, bad;

        static void Check(bool ok, string what)
        {
            checks++;
            if (!ok)
            {
                bad++;
                Console.WriteLine("⛔ " + what);
            }
        }

        static string F(double v)
        {
            return v.ToString("G4", CultureInfo.InvariantCulture);
        }

        static string Num(double v)
        {
            return v.ToString("F1", CultureInfo.InvariantCulture);
        }

        static T Field<T>(GeometrySketch s, string name)
        {
            return (T)typeof(GeometrySketch).GetField(name, Any).GetValue(s);
        }

        static Color Palette(string name)
        {
            return (Color)typeof(GeometrySketch).GetField(name, Any).GetValue(null);
        }

        // ------------------------------------------------------------------
        // Модели
        // ------------------------------------------------------------------

        static GeometryModel Preset(string name, GeometryDetectorFacing facing)
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            GeometryPresets.Items.First(p => p.Name == name).Apply(g);
            g.Facing = facing;
            return g;
        }

        static GeometryModel Nano(GeometryDetectorFacing f) { return Preset("Atom Spectra Nano 16", f); }
        static GeometryModel Rc110(GeometryDetectorFacing f) { return Preset("RadiaCode-110", f); }
        static GeometryModel NaI() { return Preset("Gamma-1S UDS-GC 63x63", GeometryDetectorFacing.Front); }

        static GeometryModel Marinelli(GeometryModel g)
        {
            g.SourceType = GeometrySourceType.Marinelli;
            g.MarinelliBeakerDiameter = 130.0;
            g.MarinelliBeakerHeight = 110.0;
            g.MarinelliSourceHeight = 100.0;
            g.MarinelliHoleDiameter = 76.0;
            g.MarinelliHoleHeight = 70.0;
            g.MarinelliSideThickness = 2.0;
            g.MarinelliHoleSideThickness = 2.0;
            g.MarinelliEndWallThickness = 2.0;
            g.MarinelliHoleEndWallThickness = 2.0;
            return g;
        }

        static GeometryModel Trapezoid(GeometryModel g)
        {
            Marinelli(g);
            g.MarinelliHoleShape = GeometryHoleShape.Trapezoid;
            g.MarinelliHoleBase1 = 80.0;
            g.MarinelliHoleBase2 = 100.0;
            g.MarinelliHoleWidth = 76.0;
            return g;
        }

        static GeometryModel Cylinder(GeometryModel g)
        {
            g.SourceType = GeometrySourceType.Cylinder;
            g.BeakerDiameter = 90.0;
            g.BeakerHeight = 60.0;
            g.SourceHeight = 50.0;
            g.BeakerSideWallThickness = 2.0;
            g.BeakerEndWallThickness = 2.0;
            return g;
        }

        /// <summary>(`AMBER324`) Чашка Петри ⌀90×10: главный размер «10» скрывался на «Источнике» в 280×646 и 420×460.</summary>
        static GeometryModel Petri(GeometryModel g)
        {
            g.SourceType = GeometrySourceType.Cylinder;
            g.BeakerDiameter = 90.0;
            g.BeakerHeight = 12.0;
            g.SourceHeight = 10.0;
            g.BeakerSideWallThickness = 1.0;
            g.BeakerEndWallThickness = 1.0;
            return g;
        }

        static GeometryModel BoxSource(GeometryModel g)
        {
            g.SourceType = GeometrySourceType.Box;
            g.BoxSourceX = 100.0;
            g.BoxSourceY = 60.0;
            g.BoxSourceHeight = 30.0;
            g.BoxSideWallThickness = 2.0;
            g.BoxEndWallThickness = 2.0;
            return g;
        }

        static GeometryModel Point(GeometryModel g)
        {
            g.SourceType = GeometrySourceType.Point;
            g.PointDistance = 100.0;
            return g;
        }

        static GeometryModel Iso(GeometryModel g)
        {
            GeometryScenes.Iso(g);
            return g;
        }

        static GeometryModel Ground(GeometryModel g)
        {
            GeometryScenes.Ground(g, 3000.0);
            return g;
        }

        static GeometryModel Borehole(GeometryModel g)
        {
            GeometryScenes.Borehole(g, 3000.0);
            return g;
        }

        static GeometryMaterial Mat(string name, int z, double density)
        {
            GeometryMaterial m = new GeometryMaterial { Name = name, Density = density };
            m.Fractions[z] = 1.0;
            return m;
        }

        static GeometryModel Shielded(GeometryModel g)
        {
            g.InShield = true;
            g.ShieldInnerDiameter = 200.0;
            g.ShieldInnerHeight = 200.0;
            g.ShieldFloorGap = 0.0;
            g.ShieldLayers.Clear();
            g.ShieldLayers.Add(new GeometryShieldLayer { Material = Mat("Copper", 29, 8.96), SideThickness = 2.0, FloorThickness = 2.0, LidThickness = 2.0 });
            g.ShieldLayers.Add(new GeometryShieldLayer { Material = Mat("Lead", 82, 11.35), SideThickness = 50.0, FloorThickness = 50.0, LidThickness = 50.0 });
            return g;
        }

        // ------------------------------------------------------------------
        // Отрисовка
        // ------------------------------------------------------------------

        enum Tab
        {
            Detector,
            Source,
            Shield,
        }

        static Bitmap Paint(GeometrySketch sketch)
        {
            Size size = sketch.Size;
            Bitmap b = new Bitmap(size.Width, size.Height);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.White);
                typeof(GeometrySketch).GetMethod("OnPaint", Any).Invoke(sketch, new object[] { new PaintEventArgs(g, new Rectangle(System.Drawing.Point.Empty, size)) });
            }

            return b;
        }

        /// <summary>Эскиз вкладки: как его настраивает редактор (`GeometryEditorPanel.AddSketch`).</summary>
        static GeometrySketch Sketch(GeometryModel m, Tab tab, Size size)
        {
            GeometrySketch s = new GeometrySketch
            {
                Mode = tab == Tab.Detector ? GeometrySketch.SketchMode.Detector : GeometrySketch.SketchMode.Source,
                Size = size,
                ShowShield = tab == Tab.Shield,
            };
            s.SetModel(m);
            return s;
        }

        /// <summary>(`AMBER325`) Эскиз «Источника» в заданном положении галки: true — со стороны пробы.</summary>
        static GeometrySketch Sketch(GeometryModel m, Tab tab, Size size, bool fromSample)
        {
            GeometrySketch s = Sketch(m, tab, size);
            if (fromSample)
            {
                SetFromSample(s, true);
            }

            return s;
        }

        // ------------------------------------------------------------------
        // (`AMBER325`) Сторона второго вида — отражением: проба собирается и против сборки до правки
        // ------------------------------------------------------------------

        static readonly PropertyInfo FromSampleProperty = typeof(GeometrySketch).GetProperty("PlanFromSample", Any);

        /// <summary>Нет свойства (сборка до `AMBER325`) — вид всегда со стороны пробы, как сделал `AMBER324`.</summary>
        static bool FromSample(GeometrySketch s)
        {
            return FromSampleProperty == null || (bool)FromSampleProperty.GetValue(s);
        }

        static void SetFromSample(GeometrySketch s, bool value)
        {
            if (FromSampleProperty != null)
            {
                FromSampleProperty.SetValue(s, value);
            }
        }

        static Rectangle CheckBounds(GeometrySketch s)
        {
            PropertyInfo p = typeof(GeometrySketch).GetProperty("PlanSideCheckBounds", Any);
            return p == null ? Rectangle.Empty : (Rectangle)p.GetValue(s);
        }

        static string Res(string name)
        {
            return Resources.ResourceManager.GetString(name, Resources.Culture);
        }

        static GeometrySketch.SketchLabel[] In(GeometrySketch s, Rectangle panel)
        {
            return s.Labels.Where(l => panel.Contains(Rectangle.Round(l.Bounds))).ToArray();
        }

        /// <summary>Охват точек цвета внутри рамки вида; пусто, если цвета нет.</summary>
        static Rectangle Span(Bitmap b, Rectangle panel, Color color)
        {
            int argb = color.ToArgb();
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = Math.Max(panel.Top, 0); y < Math.Min(panel.Bottom, b.Height); y++)
            {
                for (int x = Math.Max(panel.Left, 0); x < Math.Min(panel.Right, b.Width); x++)
                {
                    if (b.GetPixel(x, y).ToArgb() == argb)
                    {
                        x0 = Math.Min(x0, x);
                        y0 = Math.Min(y0, y);
                        x1 = Math.Max(x1, x);
                        y1 = Math.Max(y1, y);
                    }
                }
            }

            return x1 < 0 ? Rectangle.Empty : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        // ------------------------------------------------------------------
        // Заслон ГОСТ 2.307-2011
        // ------------------------------------------------------------------

        /// <summary>Пересечения частично перекрытых размеров: необходимые, в отчёт.</summary>
        static int NecessaryCrossings;

        sealed class Layout
        {
            public List<KeyValuePair<string, RectangleF>> Labels = new List<KeyValuePair<string, RectangleF>>();
            public List<GeometrySketch.DimLine> Lines = new List<GeometrySketch.DimLine>();
            public RectangleF[] Contours = new RectangleF[0];
        }

        static bool Hits(PointF a, PointF b, RectangleF r)
        {
            for (int i = 0; i <= 200; i++)
            {
                float t = i / 200f;
                if (r.Contains(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)))
                {
                    return true;
                }
            }

            return false;
        }

        static bool Cross(PointF a, PointF b, PointF c, PointF d)
        {
            Func<PointF, PointF, PointF, float> side = (p, q, r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            float d1 = side(a, b, c), d2 = side(a, b, d), d3 = side(c, d, a), d4 = side(c, d, b);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }

        /// <summary>Заслон: нарушения правил на одной раскладке. Читает только рамки подписей и линии, чтобы его можно было
        /// поставить на настоящую раскладку прошлой версии.</summary>
        static List<string> Gost(Layout l)
        {
            List<string> found = new List<string>();
            for (int i = 0; i < l.Labels.Count; i++)
            {
                RectangleF box = l.Labels[i].Value;
                string name = l.Labels[i].Key;
                for (int j = i + 1; j < l.Labels.Count; j++)
                {
                    RectangleF o = RectangleF.Intersect(box, l.Labels[j].Value);
                    if (o.Width > 1f && o.Height > 1f)
                    {
                        found.Add("подписи наложились: " + name + " × " + l.Labels[j].Key);
                    }
                }

                // Сквозь число линия не идёт (п. 5.31): меряется рамка без полей шрифта.
                RectangleF ink = box;
                ink.Inflate(-1f, -1f);
                foreach (GeometrySketch.DimLine line in l.Lines)
                {
                    if (Hits(line.A, line.B, ink))
                    {
                        found.Add("линия через число: " + name + " [" + line.Kind + " " + line.Key + " " + line.Side + " " + line.Row + "; число " + box + "; линия " + line.A + "-" + line.B + "]");
                    }
                }

                foreach (RectangleF contour in l.Contours)
                {
                    if (RectangleF.Intersect(ink, contour).Width > 0f && RectangleF.Intersect(ink, contour).Height > 0f)
                    {
                        found.Add("число на теле или контуре: " + name);
                    }
                }
            }

            foreach (GeometrySketch.DimLine ext in l.Lines.Where(x => x.Kind == GeometrySketch.DimLineKind.Extension))
            {
                foreach (GeometrySketch.DimLine dim in l.Lines.Where(x => x.Kind == GeometrySketch.DimLineKind.Dimension && x.Group != ext.Group && x.OnTopView == ext.OnTopView))
                {
                    if (!Cross(ext.A, ext.B, dim.A, dim.B))
                    {
                        continue;
                    }

                    // По оси ряда: где выносная встречает размерную. На границе размера это их общая выносная.
                    // Внутри размера — размеры частично перекрыты по оси и пересечение необходимо (п. 5.12, «избегать»):
                    // считается отдельно. На хвосте и на продолжении без нужды: это и есть нарушение.
                    bool h = Math.Abs(dim.A.Y - dim.B.Y) < 0.5f;
                    float at = h ? ext.A.X : ext.A.Y;
                    if (Math.Abs(at - dim.SpanA) <= 1f || Math.Abs(at - dim.SpanB) <= 1f)
                    {
                        continue;
                    }

                    if (at > dim.SpanA && at < dim.SpanB)
                    {
                        NecessaryCrossings++;
                        continue;
                    }

                    found.Add("выносная пересекает размерную: " + ext.Key + " × " + dim.Key);
                }
            }

            return found;
        }

        static Layout LayoutOf(GeometrySketch s)
        {
            Layout l = new Layout();
            foreach (GeometrySketch.SketchLabel label in s.Labels)
            {
                l.Labels.Add(new KeyValuePair<string, RectangleF>(label.Text, label.Bounds));
            }

            l.Lines.AddRange(s.DimensionLines);
            l.Contours = s.TopContour.IsEmpty ? new[] { s.SideContour } : new[] { s.SideContour, s.TopContour };
            return l;
        }

        /// <summary>Настоящая раскладка прошлой версии: RC-110, «Проба у широкой грани», поле 280×427. Снята с прежнего кода,
        /// в журнале снимок `old_rc110`. Числа стоят внутри тела, размерные линии без выносных.</summary>
        static Layout OldRc110()
        {
            Layout l = new Layout();
            string[] labels =
            {
                "14|131.2|47.2|16.3|13.8", "14|220.7|140.1|16.3|13.8", "0.065|127.2|94.1|32.0|13.8", "3|157.6|65.2|10.0|13.8",
                "0.065|110.6|122.0|32.0|13.8", "3|106.5|140.3|10.0|13.8", "2.985|79.0|79.7|32.0|13.8", "0.785|64.5|130.8|32.0|13.8", "1.5|142.4|161.9|19.4|13.8",
            };
            foreach (string row in labels)
            {
                string[] p = row.Split('|');
                Func<int, float> v = i => float.Parse(p[i], CultureInfo.InvariantCulture);
                l.Labels.Add(new KeyValuePair<string, RectangleF>(p[0], new RectangleF(v(1), v(2), v(3), v(4))));
            }

            float[,] lines =
            {
                { 105.6f, 62f, 173.1f, 62f }, { 217.7f, 113.2f, 217.7f, 180.7f }, { 124.2f, 112.9f, 124.2f, 113.2f }, { 154.6f, 84f, 154.6f, 98.5f },
                { 105.3f, 136.8f, 105.6f, 136.8f }, { 87f, 155.1f, 101.5f, 155.1f }, { 114f, 98.5f, 114f, 112.9f }, { 101.5f, 145.6f, 105.3f, 145.6f }, { 139.4f, 180.7f, 139.4f, 188f },
            };
            for (int i = 0; i < lines.GetLength(0); i++)
            {
                l.Lines.Add(new GeometrySketch.DimLine
                {
                    A = new PointF(lines[i, 0], lines[i, 1]), B = new PointF(lines[i, 2], lines[i, 3]),
                    Kind = GeometrySketch.DimLineKind.Dimension, Group = i,
                });
            }

            // Тело прошлого чертежа: корпус, по его же размерам.
            l.Contours = new[] { RectangleF.FromLTRB(87f, 84f, 191.7f, 188f) };
            return l;
        }

        static void OldLayoutRefused()
        {
            List<string> found = Gost(OldRc110());
            Check(found.Count > 0, "заслон ГОСТ не отказал прежней раскладке RC-110");
            foreach (string number in new[] { "0.065", "3", "2.985", "1.5" })
            {
                Check(found.Any(x => x.Contains(": " + number + " [") || x.EndsWith(": " + number, StringComparison.Ordinal)), "заслон ГОСТ не увидел число " + number + " прежней раскладки");
            }

            Console.WriteLine("прежняя раскладка RC-110: отказов заслона " + found.Count);
        }

        /// <summary>Ряды: постоянный шаг, вложенный ближе к контуру, вылет выносной.</summary>
        static void Rows(GeometrySketch s, string where)
        {
            GeometrySketch.DimLine[] lines = s.DimensionLines;
            foreach (bool top in new[] { false, true })
            {
                RectangleF contour = top ? s.TopContour : s.SideContour;
                foreach (GeometrySketch.DimSide side in Enum.GetValues(typeof(GeometrySketch.DimSide)))
                {
                    GeometrySketch.DimLine[] dims = lines.Where(x => x.OnTopView == top && x.Side == side && x.Row >= 0 && x.Kind == GeometrySketch.DimLineKind.Dimension).ToArray();
                    bool h = side == GeometrySketch.DimSide.Top || side == GeometrySketch.DimSide.Bottom;
                    Dictionary<int, float> rowOff = new Dictionary<int, float>();
                    foreach (GeometrySketch.DimLine d in dims)
                    {
                        float at = h ? d.A.Y : d.A.X;
                        float off = side == GeometrySketch.DimSide.Top ? contour.Top - at
                                  : side == GeometrySketch.DimSide.Bottom ? at - contour.Bottom
                                  : side == GeometrySketch.DimSide.Left ? contour.Left - at
                                  : at - contour.Right;
                        if (h)
                        {
                            double row = (off - GeometrySketch.DimFirstPx) / GeometrySketch.DimPitchPx;
                            Check(Math.Abs(row - d.Row) < 0.05, where + ": ряд " + d.Row + " не на шаге: " + off.ToString("F1", CultureInfo.InvariantCulture) + " точек от контура");
                        }
                        else
                        {
                            // (`AMBER324`, решение Amber 09.11.2026 «Разнести ряды (Рекомендую)») Сбоку шаг рядов — по ширине
                            // числа, не меньше `DimPitchPx`: ряд 0 на `DimFirstPx`, у одного ряда одна прямая, дальше — дальше.
                            float seen;
                            if (rowOff.TryGetValue(d.Row, out seen))
                            {
                                Check(Math.Abs(seen - off) < 0.05f, where + ": ряд " + d.Row + " сбоку не на одной прямой");
                            }
                            else
                            {
                                rowOff[d.Row] = off;
                            }

                            if (d.Row == 0)
                            {
                                Check(Math.Abs(off - GeometrySketch.DimFirstPx) < 0.05f, where + ": ряд 0 сбоку не на " + GeometrySketch.DimFirstPx);
                            }
                        }
                    }

                    List<int> sideRows = rowOff.Keys.OrderBy(x => x).ToList();
                    for (int i = 1; i < sideRows.Count; i++)
                    {
                        float step = rowOff[sideRows[i]] - rowOff[sideRows[i - 1]];
                        Check(step >= (sideRows[i] - sideRows[i - 1]) * GeometrySketch.DimPitchPx - 0.05f, where + ": ряды сбоку ближе шага: " + step.ToString("F1", CultureInfo.InvariantCulture));
                    }

                    // Группы одного ряда не делят участок оси; вложенный размер ближе к контуру.
                    foreach (var a in dims)
                    {
                        foreach (var b in dims)
                        {
                            if (a.Group == b.Group)
                            {
                                continue;
                            }

                            float a0 = h ? Math.Min(a.A.X, a.B.X) : Math.Min(a.A.Y, a.B.Y), a1 = h ? Math.Max(a.A.X, a.B.X) : Math.Max(a.A.Y, a.B.Y);
                            float b0 = h ? Math.Min(b.A.X, b.B.X) : Math.Min(b.A.Y, b.B.Y), b1 = h ? Math.Max(b.A.X, b.B.X) : Math.Max(b.A.Y, b.B.Y);
                            if (a.Row == b.Row)
                            {
                                Check(a1 <= b0 + 0.5f || b1 <= a0 + 0.5f, where + ": в одном ряду наложились линии " + a.Key + " и " + b.Key);
                            }

                            if (a0 >= b0 - 0.5f && a1 <= b1 + 0.5f && a1 - a0 < b1 - b0 - 1f)
                            {
                                Check(a.Row < b.Row, where + ": вложенный " + a.Key + " дальше от контура, чем " + b.Key);
                            }
                        }
                    }

                    foreach (GeometrySketch.DimLine e in lines.Where(x => x.OnTopView == top && x.Side == side && x.Kind == GeometrySketch.DimLineKind.Extension))
                    {
                        GeometrySketch.DimLine own = dims.FirstOrDefault(x => x.Group == e.Group);
                        if (own.Key == null && own.Kind == default(GeometrySketch.DimLineKind) && !dims.Any(x => x.Group == e.Group))
                        {
                            continue;
                        }

                        float tip = h ? e.B.Y : e.B.X, at = h ? own.A.Y : own.A.X;
                        Check(Math.Abs(Math.Abs(tip - at) - GeometrySketch.DimOverPx) < 0.1f, where + ": вылет выносной " + (tip - at).ToString("F1", CultureInfo.InvariantCulture));
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // (1) Покрытие
        // ------------------------------------------------------------------

        static readonly Size[] FieldSizes = { new Size(198, 367), new Size(280, 427), new Size(280, 646), new Size(798, 807) };

        /// <summary>Сверх заказанных: настоящие и тесные поля, план на них не обещан, правила те же.</summary>
        static readonly Size[] MoreSizes = { new Size(232, 440), new Size(420, 460), new Size(1218, 847), new Size(360, 300), new Size(500, 260), new Size(620, 300), new Size(460, 420) };

        static readonly KeyValuePair<string, Func<GeometryModel>>[] Crystals =
        {
            new KeyValuePair<string, Func<GeometryModel>>("куб торец", () => Rc110(GeometryDetectorFacing.Front)),
            new KeyValuePair<string, Func<GeometryModel>>("куб бок", () => Rc110(GeometryDetectorFacing.Side)),
            new KeyValuePair<string, Func<GeometryModel>>("брусок торец", () => Nano(GeometryDetectorFacing.Front)),
            new KeyValuePair<string, Func<GeometryModel>>("брусок бок", () => Nano(GeometryDetectorFacing.Side)),
            new KeyValuePair<string, Func<GeometryModel>>("цилиндр NaI", NaI),
        };

        static readonly KeyValuePair<string, Func<GeometryModel, GeometryModel>>[] Sources =
        {
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("точка", Point),
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("маринелли", Marinelli),
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("трапеция", Trapezoid),
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("банка", Cylinder),
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("кювета", BoxSource),
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("петри", Petri),
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("поле", Iso),
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("земля", Ground),
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("лунка", Borehole),
        };

        static void Coverage(string coverFile)
        {
            List<string> report = new List<string>();
            int total = 0, noPlan = 0, shieldTab = 0, hiddenMain = 0, hiddenShield = 0, hiddenTight = 0, leaderCross = 0, leaderCrossShield = 0;
            Dictionary<int, int> levels = new Dictionary<int, int>();
            Action<string, Tab, Func<GeometryModel>, Size, bool, bool> one = (name, tab, make, size, ordered, fromSample) =>
            {
                string where = name + " " + tab + (fromSample ? " (со стороны пробы)" : "") + " " + size.Width + "×" + size.Height;
                GeometrySketch s = Sketch(make(), tab, size, fromSample);
                using (Paint(s))
                {
                    // (`AMBER324`) Полка сбоку через чужую размерную: при разнесённых рядах число каждого ряда встаёт у своей линии.
                    foreach (GeometrySketch.DimLine leader in s.DimensionLines.Where(x => x.Kind == GeometrySketch.DimLineKind.Leader
                             && (x.Side == GeometrySketch.DimSide.Left || x.Side == GeometrySketch.DimSide.Right)))
                    {
                        foreach (GeometrySketch.DimLine dim in s.DimensionLines.Where(x => x.Kind == GeometrySketch.DimLineKind.Dimension && x.Group != leader.Group && x.OnTopView == leader.OnTopView))
                        {
                            if (Cross(leader.A, leader.B, dim.A, dim.B))
                            {
                                SideShelfCross++;
                                string bucket = tab + " " + size.Width + "×" + size.Height;
                                int seenCross;
                                SideShelfBySize.TryGetValue(bucket, out seenCross);
                                SideShelfBySize[bucket] = seenCross + 1;
                                if (size.Width >= 280 && size.Height >= 367 && !Compact(s))
                                {
                                    SideShelfCrossReal++;
                                    Console.WriteLine("  полка сбоку через размерную: " + where + ": " + leader.Key + " × " + dim.Key);
                                }
                            }
                        }
                    }

                    if (Compact(s))
                    {
                        string bucket = tab + " " + size.Width + "×" + size.Height;
                        int seenCompact;
                        CompactBySize.TryGetValue(bucket, out seenCompact);
                        CompactBySize[bucket] = seenCompact + 1;

                        // (`AMBER324`) Решения Amber 09.11.2026 вопросником, дословно: о проходе — «Оставить (Рекомендую)»; о
                        // маринелли в защите в окнах шириной 280 — «Крупнее, «110» на полке (Рекомендую)». Главный на полке
                        // допустим, скрытым — нет. Скрытый главный судится в настоящих полях (от 280×367): в тесных 232×440 и
                        // уже ⌀ сосуда скрывался и до `AMBER324` (там «главных скрыто» считает и печатает `Coverage`).
                        CompactDrawings++;
                        if (size.Width >= 280 && size.Height >= 367)
                        {
                            foreach (string hidden in HiddenMain(s))
                            {
                                CompactMainLost++;
                                Console.WriteLine("  второй проход, главный скрыт: " + where + ": " + hidden);
                            }
                        }

                        // Главный на полке сбоку — только счёт, для отчёта.
                        HashSet<string> main = MainKeys(s);
                        CompactMainShelf += s.DimensionLines.Where(x => x.Kind == GeometrySketch.DimLineKind.Leader && x.Key != null && main.Contains(x.Key)
                                     && (x.Side == GeometrySketch.DimSide.Left || x.Side == GeometrySketch.DimSide.Right)).Select(x => x.Key).Distinct().Count();
                    }

                    // (`AMBER324`) Повёрнутое число: рамка выше своей ширины. Однознаковое не судится — его рамка и так узкая.
                    foreach (KeyValuePair<string, RectangleF> n in s.DimensionNumbers)
                    {
                        NumbersSeen++;
                        if (Turned(n))
                        {
                            NumbersTurned++;
                            if (NumbersTurned <= 4) Console.WriteLine("  повёрнуто: " + where + ": " + n.Key);
                        }
                    }

                    Rectangle a = s.SideViewBounds, b = s.TopViewBounds;
                    if (tab == Tab.Shield)
                    {
                        shieldTab++;
                        Check(b.IsEmpty, where + ": на вкладке «Защита» план");
                    }
                    else if (ordered)
                    {
                        total++;
                        int level = s.DimensionLevel, seen;
                        levels.TryGetValue(level, out seen);
                        levels[level] = seen + 1;
                        // (`AMBER325`, второй заход, вердикт `crit325` п. 5) Уровень 0 — чертёж без единого размера, главные
                        // тоже скрыты, а «главных скрыто» считает только скрытые наложением. На заказанных полях его быть не должно.
                        if (level == 0)
                        {
                            LevelZero++;
                            Console.WriteLine("  уровень 0 (без размеров): " + where + ", чисел " + s.DimensionNumbers.Length);
                        }
                        if (b.IsEmpty)
                        {
                            noPlan++;
                            report.Add("без плана: " + where);
                        }
                    }

                    if (!b.IsEmpty)
                    {
                        Check(RectangleF.Intersect(a, b).IsEmpty, where + ": рамки пересекаются");
                        Check(b.Top - a.Bottom == 8 || b.Left - a.Right == 8, where + ": зазор не 8");
                    }

                    List<string> found = Gost(LayoutOf(s));
                    foreach (string line in found.Take(4))
                    {
                        Check(false, where + ": " + line);
                    }

                    if (ordered)
                    {
                        Picture(s, where, Palette("Canvas"));
                        foreach (GeometrySketch.DimLine leader in s.DimensionLines.Where(x => x.Kind == GeometrySketch.DimLineKind.Leader))
                        {
                            foreach (GeometrySketch.DimLine dim in s.DimensionLines.Where(x => x.Kind == GeometrySketch.DimLineKind.Dimension && x.Group != leader.Group && x.OnTopView == leader.OnTopView))
                            {
                                if (Cross(leader.A, leader.B, dim.A, dim.B))
                                {
                                    if (tab == Tab.Shield) leaderCrossShield++; else leaderCross++;
                                }
                            }
                        }
                    }

                    checks++;
                    Rows(s, where);
                    foreach (GeometrySketch.SketchLabel label in s.Labels)
                    {
                        RectangleF r = label.Bounds;
                        Check(r.Left >= 0f && r.Top >= 0f && r.Right <= size.Width && r.Bottom <= size.Height, where + ": подпись вне поля: " + label.Text);
                        if (!a.IsEmpty && r.Top < Math.Max(a.Bottom, b.Bottom) - 1)
                        {
                            RectangleF ra = RectangleF.Intersect(r, a), rb = RectangleF.Intersect(r, b);
                            Check(Whole(ra, r) || Whole(rb, r), where + ": подпись не в своей рамке: " + label.Text);
                        }
                    }

                    foreach (string hidden in s.HiddenDimensions)
                    {
                        if (hidden.EndsWith("ранг 1)", StringComparison.Ordinal))
                        {
                            if (tab == Tab.Shield)
                            {
                                hiddenShield++;
                            }
                            else if (size.Height >= 367)
                            {
                                hiddenMain++;
                            }
                            else
                            {
                                hiddenTight++;
                            }

                            report.Add("главный размер скрыт: " + where + ": " + hidden);
                        }
                    }
                }
            };
            foreach (Size size in FieldSizes.Concat(MoreSizes))
            {
                bool ordered = FieldSizes.Contains(size);
                foreach (var crystal in Crystals)
                {
                    one(crystal.Key, Tab.Detector, crystal.Value, size, ordered, false);
                    foreach (var source in Sources)
                    {
                        KeyValuePair<string, Func<GeometryModel>> c = crystal;
                        KeyValuePair<string, Func<GeometryModel, GeometryModel>> z = source;
                        // (`AMBER325`) Оба положения галки: со стороны детектора (умолчание) и со стороны пробы.
                        foreach (bool fromSample in new[] { false, true })
                        {
                            one(c.Key + " + " + z.Key, Tab.Source, () => z.Value(c.Value()), size, ordered, fromSample);
                        }
                    }

                    KeyValuePair<string, Func<GeometryModel>> c2 = crystal;
                    one(c2.Key + " в защите, точка", Tab.Shield, () => Shielded(Point(c2.Value())), size, ordered, false);
                    one(c2.Key + " в защите, маринелли", Tab.Shield, () => Shielded(Marinelli(c2.Value())), size, ordered, false);
                }
            }

            string head = "покрытие: на «Детекторе» и «Источнике» чертежей " + total + ", без плана " + noPlan
                          + "; главных размеров скрыто " + hiddenMain + " (в полях ниже настоящих, 260–300 точек, ещё " + hiddenTight + ")" + "; на «Защите» чертежей " + shieldTab + ", скрыто главных " + hiddenShield
                          + "; необходимых пересечений " + NecessaryCrossings + "; выноска через размерную: источник и детектор " + leaderCross + ", защита " + leaderCrossShield + "; уровни: " + string.Join(", ", levels.OrderByDescending(x => x.Key).Select(x => x.Key + "=" + x.Value));
            Console.WriteLine(head);
            report.Insert(0, head);
            if (coverFile != null)
            {
                File.WriteAllLines(coverFile, report, new UTF8Encoding(false));
            }

            Check(noPlan == 0, "без плана чертежей " + noPlan);
            Check(LevelZero == 0, "уровень 0 (без размеров) на заказанных полях: чертежей " + LevelZero);
            Check(hiddenMain == 0, "главных размеров скрыто " + hiddenMain);
            Console.WriteLine("числа покрытия (`AMBER324`): всего " + NumbersSeen + ", повёрнутых " + NumbersTurned + "; полка сбоку через чужую размерную " + SideShelfCross);
            Console.WriteLine("  полка сбоку через чужую размерную по полям: " + string.Join(", ", SideShelfBySize.OrderBy(x => x.Key).Select(x => x.Key + " " + x.Value)));
            Console.WriteLine("  второй проход (ряды сбоку прежним шагом, тело иначе мельче порога) по полям: " + string.Join(", ", CompactBySize.OrderBy(x => x.Key).Select(x => x.Key + " " + x.Value)));
            // (`AMBER324`, «Разнести ряды») В настоящих полях окна (от 280×367) число каждого ряда сбоку встаёт у своей линии,
            // полка через чужую размерную — ноль. Где тело с разнесёнными рядами мельче порога, второй проход раскладки
            // возвращает прежний шаг рядов (`compactRows`), и полки там допустимы: счёт печатается, заслоном не служит.
            // (`AMBER325`) Галка в строке заголовка раскладку «Источника» не трогает: второго прохода там нет (вердикт `crit325`).
            Check(!CompactBySize.Keys.Any(k => k.StartsWith("Source", StringComparison.Ordinal)), "второй проход раскладки на «Источнике»");
            Check(SideShelfCrossReal == 0, "полка сбоку через чужую размерную в настоящих полях: " + SideShelfCrossReal);
            Console.WriteLine("  второй проход: чертежей " + CompactDrawings + ", главных скрыто " + CompactMainLost + ", главных на полке " + CompactMainShelf);
            Check(CompactMainLost == 0, "второй проход: главных скрыто " + CompactMainLost);

            // Положительный контроль счёта «главный скрыт»: чертёж, на котором главный скрыт заведомо (маринелли на «Источнике»
            // в поле 500×260 — ⌀ колодца, было и до `AMBER324`), обязан дать не ноль тем же `HiddenMain`.
            GeometrySketch control = Sketch(Marinelli(Rc110(GeometryDetectorFacing.Front)), Tab.Source, new Size(500, 260));
            using (Paint(control))
            {
                Console.WriteLine("  контроль «главный скрыт»: " + string.Join("; ", HiddenMain(control)));
                Check(HiddenMain(control).Count > 0, "контроль: счёт «главный скрыт» не увидел скрытый главный");
            }
            Check(NumbersSeen > 0 && NumbersTurned == 0, "повёрнутых чисел " + NumbersTurned + " из " + NumbersSeen);
        }

        static int NumbersSeen, NumbersTurned, SideShelfCross, SideShelfCrossReal, LevelZero;

        /// <summary>(`AMBER324`) Второй проход раскладки; у сборки до правки поля нет — значит нет.</summary>
        static bool Compact(GeometrySketch s)
        {
            FieldInfo f = typeof(GeometrySketch).GetField("compactRows", Any);
            return f != null && (bool)f.GetValue(s);
        }

        static readonly Dictionary<string, int> SideShelfBySize = new Dictionary<string, int>();

        static readonly Dictionary<string, int> CompactBySize = new Dictionary<string, int>();

        static int CompactDrawings, CompactMainLost, CompactMainShelf;

        /// <summary>(`AMBER324`) Скрытые главные размеры (ранг 1) последней отрисовки.</summary>
        static List<string> HiddenMain(GeometrySketch s)
        {
            return s.HiddenDimensions.Where(x => x.EndsWith("ранг 1)", StringComparison.Ordinal)).ToList();
        }

        /// <summary>(`AMBER324`) Ключи главных размеров (ранг 1) последней отрисовки — из очереди размеров чертежа.</summary>
        static HashSet<string> MainKeys(GeometrySketch s)
        {
            HashSet<string> set = new HashSet<string>();
            foreach (object group in (System.Collections.IEnumerable)typeof(GeometrySketch).GetField("dimGroups", Any).GetValue(s))
            {
                foreach (object span in (System.Collections.IEnumerable)group.GetType().GetField("Spans", Any).GetValue(group))
                {
                    int rank = (int)span.GetType().GetField("Rank", Any).GetValue(span);
                    string key = (string)span.GetType().GetField("Key", Any).GetValue(span);
                    if (rank == 1 && key != null)
                    {
                        set.Add(key);
                    }
                }
            }

            return set;
        }

        /// <summary>(`AMBER324`) Пояснение о штрихе. По имени ключа, а не свойством: проба собирается и против сборки до правки
        /// (положительный контроль), где ключа нет; там — строка, которой в тексте не бывает.</summary>
        static string HiddenHint
        {
            get { return Resources.ResourceManager.GetString("EfficiencySketchHiddenHint", Resources.Culture) ?? ""; }
        }

        static bool Turned(KeyValuePair<string, RectangleF> n)
        {
            return n.Key.Length >= 2 && n.Value.Height > n.Value.Width;
        }

        /// <summary>
        /// (`AMBER324`) Положительный контроль правила `Turned`: рамка повёрнутого числа прежней раскладки (`t × w`) обязана
        /// судиться повёрнутой, прямая двузначная — нет; и по чернилам: строка, напечатанная прежним поворотом, выше своей
        /// ширины, напечатанная прямо — шире.
        /// </summary>
        static void TurnedControl()
        {
            using (Bitmap b = new Bitmap(120, 120))
            using (Graphics g = Graphics.FromImage(b))
            {
                Font font = new GeometrySketch().Font;
                foreach (string text in new[] { "20", "21.7", "⌀130" })
                {
                    SizeF size = g.MeasureString(text, font);
                    Check(!Turned(new KeyValuePair<string, RectangleF>(text, new RectangleF(0f, 0f, size.Width, size.Height))), "контроль: прямое «" + text + "» судится повёрнутым");
                    Check(Turned(new KeyValuePair<string, RectangleF>(text, new RectangleF(0f, 0f, size.Height, size.Width))), "контроль: повёрнутое «" + text + "» не судится повёрнутым");
                    foreach (bool rotate in new[] { false, true })
                    {
                        g.Clear(Color.White);
                        GraphicsState state = g.Save();
                        if (rotate)
                        {
                            // Ровно как прежний `DrawDimLines`: перенос в левый нижний угол рамки и поворот на −90°.
                            g.TranslateTransform(20f, 100f);
                            g.RotateTransform(-90f);
                            g.DrawString(text, font, Brushes.Black, 0f, 0f);
                        }
                        else
                        {
                            g.DrawString(text, font, Brushes.Black, 20f, 20f);
                        }

                        g.Restore(state);
                        Rectangle ink = DarkSpan(b);
                        Check(!ink.IsEmpty && (ink.Height > ink.Width) == rotate, "контроль чернил: «" + text + "» " + (rotate ? "повёрнутое" : "прямое") + " " + ink.Size);
                    }
                }
            }
        }

        static Rectangle DarkSpan(Bitmap b)
        {
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
            for (int y = 0; y < b.Height; y++)
            {
                for (int x = 0; x < b.Width; x++)
                {
                    Color c = b.GetPixel(x, y);
                    if (c.R + c.G + c.B < 384)
                    {
                        x0 = Math.Min(x0, x);
                        y0 = Math.Min(y0, y);
                        x1 = Math.Max(x1, x);
                        y1 = Math.Max(y1, y);
                    }
                }
            }

            return x1 < 0 ? Rectangle.Empty : Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        static bool Whole(RectangleF part, RectangleF all)
        {
            return part.Width >= all.Width - 0.5f && part.Height >= all.Height - 0.5f;
        }

        // ------------------------------------------------------------------
        // (2) Числа плана и (3) то же в точках
        // ------------------------------------------------------------------

        static void Numbers(string name, GeometryModel m, double expectX, double expectY, double expectDepth, double expectWrap)
        {
            double hx, hy, depth;
            string kx, ky, kd;
            m.CrystalBoxInScene(out hx, out hy, out depth, out kx, out ky, out kd);
            Check(Math.Abs(2.0 * hx - expectX) < 1e-9 && Math.Abs(2.0 * hy - expectY) < 1e-9 && Math.Abs(depth - expectDepth) < 1e-9,
                  name + ": CrystalBoxInScene = " + F(2.0 * hx) + "×" + F(2.0 * hy) + " глубина " + F(depth));

            double toSample, lateral;
            GeometryScenes.WrappingInScene(m, out toSample, out lateral);
            Check(Math.Abs(lateral - expectWrap) < 1e-9, name + ": WrappingInScene " + F(lateral) + " против " + F(expectWrap));

            GeometrySketch s = Sketch(m, Tab.Detector, new Size(798, 807));
            using (Bitmap b = Paint(s))
            {
                Rectangle top = s.TopViewBounds, side = s.SideViewBounds;
                GeometrySketch.SketchLabel[] onTop = In(s, top);
                Check(onTop.Any(l => l.Key == kx && l.Text == F(expectX)), name + ": на плане нет X " + F(expectX) + " с ключом " + kx);
                Check(onTop.Any(l => l.Key == ky && l.Text == F(expectY)), name + ": на плане нет Y " + F(expectY) + " с ключом " + ky);
                bool rotated = m.Facing == GeometryDetectorFacing.Side;
                Check(onTop.Any(l => l.Key == kd && l.Text.Contains(F(expectDepth))) == rotated,
                      name + ": глубина " + F(expectDepth) + " на плане " + (rotated ? "не названа" : "названа без разворота"));

                // Y живёт на плане: надпись «Y =» на разрезе снята, иначе число стояло бы дважды.
                Check(!In(s, side).Any(l => l.Text.StartsWith("Y =", StringComparison.Ordinal)), name + ": на разрезе осталась надпись Y =");

                double scale = Field<double>(s, "scale");
                Rectangle crystal = Span(b, top, Palette("CrystalColor"));
                Check(Math.Abs(crystal.Width - expectX * scale) <= Pen, name + ": ширина кристалла на плане " + crystal.Width + " точек против " + Num(expectX * scale));
                Check(Math.Abs(crystal.Height - expectY * scale) <= Pen, name + ": высота кристалла на плане " + crystal.Height + " точек против " + Num(expectY * scale));
                Rectangle outer = Span(b, top, Palette("CladdingColor"));
                Check(Math.Abs(outer.Width - (expectX + 2.0 * expectWrap) * scale) <= Pen, name + ": корпус по X " + outer.Width + " против " + Num((expectX + 2.0 * expectWrap) * scale));
                Check(Math.Abs(outer.Height - (expectY + 2.0 * expectWrap) * scale) <= Pen, name + ": корпус по Y " + outer.Height + " против " + Num((expectY + 2.0 * expectWrap) * scale));

                // Масштаб общий: одна и та же сторона кристалла на обоих видах одной длины.
                Rectangle inSide = Span(b, side, Palette("CrystalColor"));
                Check(Math.Abs(inSide.Width - crystal.Width) <= Pen, name + ": масштаб видов разный: " + inSide.Width + " и " + crystal.Width);
                Check(Math.Abs(inSide.Height - expectDepth * scale) <= Pen, name + ": глубина на разрезе " + inSide.Height + " точек");
            }
        }

        static double Lateral(GeometryModel m)
        {
            bool side = m.Facing == GeometryDetectorFacing.Side;
            return side ? m.FrontReflectorThickness + m.FrontGapThickness + m.FrontCladdingThickness
                        : m.SideReflectorThickness + m.SideGapThickness + m.SideCladdingThickness;
        }

        static void AllNumbers()
        {
            GeometryDetectorFacing top = GeometryDetectorFacing.Front, side = GeometryDetectorFacing.Side;

            // Вписано руками из шаблонов: Nano 16 15×18×60; RC-110 куб 14, зазор спереди 0.785, сбоку 2.985.
            Numbers("брусок торец", Nano(top), 15.0, 18.0, 60.0, Lateral(Nano(top)));
            Numbers("брусок бок", Nano(side), 18.0, 60.0, 15.0, Lateral(Nano(side)));
            Numbers("куб торец", Rc110(top), 14.0, 14.0, 14.0, 0.065 + 2.985 + 3.0);
            Numbers("куб бок", Rc110(side), 14.0, 14.0, 14.0, 0.065 + 0.785 + 3.0);

            // Узкое окно: разрез не зажат постоянными полями 78 (при 198 точках они оставляли телу 42).
            GeometrySketch narrow = Sketch(Rc110(top), Tab.Detector, new Size(198, 367));
            using (Bitmap b = Paint(narrow))
            {
                Rectangle body = Span(b, narrow.SideViewBounds, Palette("CladdingColor"));
                Check(body.Width >= 50, "узкое окно: тело разреза " + body.Width + " точек");
            }
        }

        // ------------------------------------------------------------------
        // (4) Смена положения на том же контроле
        // ------------------------------------------------------------------

        static void Rebuild()
        {
            GeometryModel m = Nano(GeometryDetectorFacing.Front);
            GeometrySketch s = Sketch(m, Tab.Detector, new Size(798, 807));
            Rectangle front, turned;
            double scaleFront, scaleTurned;
            using (Bitmap b = Paint(s))
            {
                front = Span(b, s.TopViewBounds, Palette("CrystalColor"));
                scaleFront = Field<double>(s, "scale");
            }

            m.Facing = GeometryDetectorFacing.Side;
            s.SetModel(m);
            using (Bitmap b = Paint(s))
            {
                turned = Span(b, s.TopViewBounds, Palette("CrystalColor"));
                scaleTurned = Field<double>(s, "scale");
            }

            Check(Math.Abs(front.Height / scaleFront - 18.0) < 1.0, "смена положения: до разворота высота " + F(front.Height / scaleFront) + " мм");
            Check(Math.Abs(turned.Width / scaleTurned - 18.0) < 1.0 && Math.Abs(turned.Height / scaleTurned - 60.0) < 1.5,
                  "смена положения: вид не перестроился: " + F(turned.Width / scaleTurned) + "×" + F(turned.Height / scaleTurned) + " мм");

            // Пропорция плана своя, а не квадрат: у бруска рамка не съедает чертёж.
            Check(turned.Height > 2 * turned.Width, "смена положения: план не вытянулся");
        }

        // ------------------------------------------------------------------
        // (5) Подсветка поля: там, где размер стоит
        // ------------------------------------------------------------------

        static void LitOn(string name, GeometryModel m, Tab tab, string key, bool onSide, bool onTop)
        {
            GeometrySketch s = Sketch(m, tab, new Size(798, 807));
            using (Paint(s))
            {
            }

            s.HighlightKey = key;
            using (Bitmap b = Paint(s))
            {
                Color lit = Palette("LitColor");
                Check(Span(b, s.SideViewBounds, lit).IsEmpty != onSide, name + ": поле " + key + (onSide ? " не подсвечено" : " подсвечено") + " на разрезе");
                Check(Span(b, s.TopViewBounds, lit).IsEmpty != onTop, name + ": поле " + key + (onTop ? " не подсвечено" : " подсвечено") + " на плане");
            }
        }

        static void Highlight()
        {
            GeometryDetectorFacing top = GeometryDetectorFacing.Front, side = GeometryDetectorFacing.Side;
            double hx, hy, depth;
            string kx, ky, kd;
            Nano(side).CrystalBoxInScene(out hx, out hy, out depth, out kx, out ky, out kd);
            LitOn("брусок бок X", Nano(side), Tab.Detector, kx, true, true);
            LitOn("брусок бок Y", Nano(side), Tab.Detector, ky, false, true);
            LitOn("брусок бок глубина", Nano(side), Tab.Detector, kd, true, true);
            Nano(top).CrystalBoxInScene(out hx, out hy, out depth, out kx, out ky, out kd);
            LitOn("брусок торец X", Nano(top), Tab.Detector, kx, true, true);
            LitOn("цилиндр диаметр", NaI(), Tab.Detector, "CrystalDiameter", true, true);
            LitOn("торец корпус", Rc110(top), Tab.Detector, "SideCladdingThickness", true, false);
            LitOn("бок корпус", Rc110(side), Tab.Detector, "FrontCladdingThickness", true, false);
            LitOn("маринелли сосуд", Marinelli(NaI()), Tab.Source, "MarinelliBeakerDiameter", true, true);
            // (`AMBER324`) ⌀ круглого колодца на плане не ставится: колодец закрыт пробой, размер стоит на разрезе.
            LitOn("маринелли колодец", Marinelli(NaI()), Tab.Source, "MarinelliHoleDiameter", true, false);
            LitOn("маринелли стенка", Marinelli(NaI()), Tab.Source, "MarinelliSideThickness", true, false);
            LitOn("цилиндр сосуд", Cylinder(NaI()), Tab.Source, "BeakerDiameter", true, true);
            LitOn("кювета X", BoxSource(Rc110(top)), Tab.Source, "BoxSourceX", true, true);
            LitOn("кювета Y", BoxSource(Rc110(top)), Tab.Source, "BoxSourceY", false, true);
            LitOn("точка", Point(NaI()), Tab.Source, "PointDistance", true, false);

            // Фокус показывает любой размер на любом поле (мера критика: 232 размера в 198×367).
            foreach (Tab tab in new[] { Tab.Detector, Tab.Source })
            {
                foreach (var crystal in Crystals)
                {
                    foreach (var source in Sources)
                    {
                        GeometryModel m = source.Value(crystal.Value());
                        GeometrySketch wide = Sketch(m, tab, new Size(798, 807));
                        using (Paint(wide))
                        {
                        }

                        foreach (string key in wide.Labels.Where(x => x.Key != null).Select(x => x.Key).Distinct().ToArray())
                        {
                            GeometrySketch tiny = Sketch(m, tab, new Size(198, 367));
                            tiny.HighlightKey = key;
                            using (Paint(tiny))
                            {
                                Check(tiny.Labels.Any(x => x.Key == key), crystal.Key + " + " + source.Key + " " + tab + ": фокус не показал " + key);
                            }
                        }
                    }
                }
            }

            // Вторичный размер, скрытый в малом окне, фокус показывает, а чертёж не двигается.
            GeometrySketch small = Sketch(Marinelli(Rc110(top)), Tab.Source, new Size(198, 367));
            double scale;
            using (Paint(small))
            {
                scale = Field<double>(small, "scale");
            }

            bool hiddenBefore = !small.Labels.Any(l => l.Key == "MarinelliEndWallThickness");
            small.HighlightKey = "MarinelliEndWallThickness";
            using (Paint(small))
            {
                Check(Math.Abs(Field<double>(small, "scale") - scale) < 1e-9, "фокус сдвинул масштаб чертежа");
                if (hiddenBefore)
                {
                    Check(small.Labels.Any(l => l.Key == "MarinelliEndWallThickness"), "скрытый размер не показан по фокусу");
                }
            }
        }

        // ------------------------------------------------------------------
        // (6) Подпись разворота на двух языках
        // ------------------------------------------------------------------

        static string Joined(GeometrySketch s, Rectangle panel)
        {
            return string.Join(" ", In(s, panel).OrderBy(l => l.Bounds.Top).Select(l => l.Text));
        }

        static void Captions(string culture, string facing, string topTitle, string detectorTitle, string checkText, string sideTitle, string depthWord)
        {
            Resources.Culture = new CultureInfo(culture);
            Check(Resources.EfficiencySketchFacingSide == facing, culture + ": строка разворота: " + Resources.EfficiencySketchFacingSide);
            // (`AMBER325`) Строки нового вида и галки — по именам ключей: у сборки до правки их нет, и это отказ.
            Check(Res("EfficiencySketchDetectorSideView") == detectorTitle, culture + ": заголовок вида со стороны детектора: " + Res("EfficiencySketchDetectorSideView"));
            Check(Res("EfficiencySketchFromSampleCheck") == checkText, culture + ": подпись галки: " + Res("EfficiencySketchFromSampleCheck"));
            foreach (Tab tab in new[] { Tab.Detector, Tab.Source })
            {
                foreach (bool fromSample in tab == Tab.Source ? new[] { false, true } : new[] { false })
                foreach (Size size in FieldSizes.Concat(MoreSizes))
                {
                    string where = culture + " " + tab + (fromSample ? " (со стороны пробы)" : "") + " " + size.Width + ": ";
                    bool fromDetector = tab == Tab.Source && !fromSample;
                    GeometrySketch s = Sketch(Point(Nano(GeometryDetectorFacing.Side)), tab, size, fromSample);
                    using (Paint(s))
                    {
                        string text = s.TopViewBounds.IsEmpty ? string.Join(" ", s.Labels.Select(l => l.Text)) : Joined(s, s.TopViewBounds);
                        if (!s.TopViewBounds.IsEmpty)
                        {
                            string title = fromDetector ? detectorTitle : topTitle;
                            Check(text.StartsWith(title, StringComparison.Ordinal), where + "заголовок плана: " + text);
                            Check(Joined(s, s.SideViewBounds).StartsWith(sideTitle, StringComparison.Ordinal), where + "заголовок разреза");
                            // (`AMBER325`) Галка — на «Источнике», сразу под заголовком плана; на «Детекторе» её нет.
                            Check(text.Contains(checkText) == (tab == Tab.Source), where + "галка «" + checkText + "»: " + text);
                        }

                        Check(text.Contains(facing), where + "нет подписи разворота: " + text);
                        Check(text.Contains(depthWord), where + "нет глубины: " + text);
                        // (`AMBER324`) Со стороны пробы точка ничего не закрывает: о штрихе ни слова. (`AMBER325`) Со стороны детектора
                        // прибор закрывает точку — штриховой кружок, и пояснение о штрихе есть, где пояснение пишется вовсе.
                        bool hintShown = !s.TopViewBounds.IsEmpty && s.TopViewBounds.Width >= 300;
                        Check(text.Contains(HiddenHint) == (fromDetector && hintShown), where + "пояснение о штрихе: " + text);
                    }

                    GeometrySketch c = Sketch(Cylinder(Nano(GeometryDetectorFacing.Side)), tab, size, fromSample);
                    using (Paint(c))
                    {
                        string text = c.TopViewBounds.IsEmpty ? "" : Joined(c, c.TopViewBounds);
                        if (size.Width >= 798 && !c.TopViewBounds.IsEmpty)
                        {
                            // Банка ⌀90 шире грани бруска: со стороны пробы она закрывает прибор (штрих), со стороны детектора
                            // прибор её контура не закрывает (штриха нет).
                            Check(text.Contains(HiddenHint) == (tab == Tab.Source && fromSample), where + "пояснение о штрихе: " + text);
                        }
                    }

                    GeometrySketch f = Sketch(Point(Nano(GeometryDetectorFacing.Front)), tab, size);
                    using (Paint(f))
                    {
                        string text = string.Join(" ", f.Labels.Select(l => l.Text));
                        Check(!text.Contains(facing) && !text.Contains(depthWord), where + "подпись разворота без разворота: " + text);
                    }
                }
            }

            // Один разрез: вкладка «Защита». Подпись и глубина обязаны стоять и здесь.
            foreach (Size size in FieldSizes)
            {
                GeometrySketch s = Sketch(Shielded(Point(Nano(GeometryDetectorFacing.Side))), Tab.Shield, size);
                using (Paint(s))
                {
                    string text = string.Join(" ", s.Labels.OrderBy(l => l.Bounds.Top).Select(l => l.Text));
                    Check(text.Contains(facing), culture + " «Защита» " + size.Width + ": нет подписи разворота: " + text);
                    Check(text.Contains(depthWord), culture + " «Защита» " + size.Width + ": нет глубины: " + text);
                }
            }

            Resources.Culture = null;
        }

        // ------------------------------------------------------------------
        // (7) Источник в плане: числа и пиксели
        // ------------------------------------------------------------------

        static GeometrySketch.SketchLabel[] Top(GeometryModel m, out GeometrySketch s, bool fromSample = true)
        {
            s = Sketch(m, Tab.Source, new Size(798, 807), fromSample);
            using (Paint(s))
            {
            }

            return In(s, s.TopViewBounds);
        }

        static void Has(GeometrySketch.SketchLabel[] labels, string name, string key, double value)
        {
            Check(labels.Any(l => l.Key == key && (l.Text == F(value) || l.Text == "⌀" + F(value))), name + ": на плане нет " + key + " = " + F(value));
        }

        static bool Near(Color a, Color b, int tolerance = 24)
        {
            return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) <= tolerance;
        }

        /// <summary>Сколько точек окружности радиуса r, из 720, близки к цвету.</summary>
        static int OnCircle(Bitmap b, PointF c, double r, Color color)
        {
            int hits = 0;
            for (int i = 0; i < 720; i++)
            {
                double a = i * Math.PI / 360.0;
                int x = (int)Math.Round(c.X + r * Math.Cos(a)), y = (int)Math.Round(c.Y + r * Math.Sin(a));
                if (x >= 0 && y >= 0 && x < b.Width && y < b.Height && Near(b.GetPixel(x, y), color))
                {
                    hits++;
                }
            }

            return hits;
        }

        /// <summary>Сколько точек цвета в строке растра на ширину рамки.</summary>
        static int OnRow(Bitmap b, Rectangle panel, int y, Color color)
        {
            int hits = 0;
            for (int x = panel.Left; x < panel.Right; x++)
            {
                if (Near(b.GetPixel(x, y), color))
                {
                    hits++;
                }
            }

            return hits;
        }

        /// <summary>(`AMBER324`) Половина кристалла на плане и обвязка вокруг — тем же методом, что рисует чертёж.</summary>
        static void PlanCrystalOf(GeometrySketch s, GeometryModel m, out double hx, out double wrap)
        {
            object[] a = { m, 0.0, 0.0, 0.0, 0.0, 0.0 };
            typeof(GeometrySketch).GetMethod("PlanCrystal", Any).Invoke(s, a);
            hx = (double)a[1];
            wrap = (double)a[3] + (double)a[4] + (double)a[5];
        }

        /// <summary>
        /// (`AMBER324`) Окружность контура: доля 720 точек, где в ±1.5 точки поперёк есть тёмная краска (сумма RGB < 330:
        /// контур 1.2 точки краской 0x202020 даже вполовину на светлом отражателе даёт ~300, проба и кристалл ~390).
        /// Штриховая линия (невидимый контур, ГОСТ 2.303-68) даёт долю между 0.35 и 0.85 (мерено 0.71), сплошная — не ниже 0.95
        /// (мерено 0.99); прежний штриховой кружок точки r=5 — 0.88, прежний сплошной контур под пробой — 0.93–0.97.
        /// </summary>
        static void DashedRing(Bitmap b, PointF c, double r, string what, bool dashed)
        {
            int dark = DarkOnRing(b, c, r);
            double share = dark / 720.0;
            Console.WriteLine("  " + what + ": тёмных " + dark + " из 720");
            Check(dashed ? share > 0.35 && share < 0.85 : share >= 0.95, what + (dashed ? " не штриховой" : " не сплошной") + ": тёмных " + dark + " из 720");
        }

        /// <summary>
        /// (`AMBER325`, второй заход, вердикт `crit325` п. 3) Сколько из 720 направлений несут линию на окружности радиуса r.
        /// Линия — по КОНТРАСТУ, а не по абсолютной яркости: самая тёмная точка полосы ±1.5 точки поперёк темнее фона хотя бы на
        /// 30 (сумма RGB). Фон — по каждую сторону самая светлая из точек на 3, 4, 5 точек от окружности (тонкая чужая линия рядом
        /// фон не портит), из двух сторон — более тёмная: граница двух заливок без линии (проба 374 и стенка 611) линией не мерится.
        /// Прежний абсолютный порог (сумма < 330) на светлом (холст, стенка ~615) мерил сплошную линию между двумя точками растра
        /// штриховой (полутон ~355), а на тёмном (проба 374, кристалл 391) мог бы мерить линию где её нет. Положительный контроль
        /// на синтетике обоих фонов — `DashControl`. Почему 30: линия 1.2 точки хоть одной точке растра даёт не меньше 0.6 своей
        /// краски (0.6·96 + 0.4·611 = 302 при фоне 374 у границы проба/стенка), а сглаживание границы двух заливок темнее более
        /// тёмной из них не бывает; при 60 сплошная по границе проба/стенка мерилась 678–681 из 720.
        /// </summary>
        static int DarkOnRing(Bitmap b, PointF c, double r)
        {
            Func<double, double, int> sum = (rr, a) =>
            {
                int x = (int)Math.Round(c.X + rr * Math.Cos(a)), y = (int)Math.Round(c.Y + rr * Math.Sin(a));
                if (x < 0 || y < 0 || x >= b.Width || y >= b.Height)
                {
                    return 765;
                }

                Color p = b.GetPixel(x, y);
                return p.R + p.G + p.B;
            };
            int dark = 0;
            for (int i = 0; i < 720; i++)
            {
                double a = i * Math.PI / 360.0;
                int band = new[] { -1.5, -1.0, -0.5, 0.0, 0.5, 1.0, 1.5 }.Min(dr => sum(r + dr, a));
                int inner = new[] { 3.0, 4.0, 5.0 }.Max(d => r - d > 0.0 ? sum(r - d, a) : 0);
                int outer = new[] { 3.0, 4.0, 5.0 }.Max(d => sum(r + d, a));
                if (band < Math.Min(inner, outer) - 30)
                {
                    dark++;
                }
            }

            return dark;
        }

        /// <summary>
        /// (`AMBER325`, второй заход) Положительный контроль меры `DarkOnRing` на синтетике: тем же пером, что чертёж (`HiddenPen`
        /// отражением и `Pen(Ink, 1.2)`), с тем же сглаживанием, на пяти фонах (холст/стенка, корпус/холст, кристалл, проба,
        /// проба/стенка), радиусах 5–151 точки и четырёх подпиксельных сдвигах центра: штрих обязан мериться штрихом (0.35–0.85),
        /// сплошная — сплошной (≥ 0.95), граница заливок без линии — пустотой (≤ 0.05).
        /// </summary>
        static void DashControl()
        {
            Color ink = Palette("Ink");
            KeyValuePair<string, Color[]>[] grounds =
            {
                new KeyValuePair<string, Color[]>("холст/стенка", new[] { Palette("Canvas"), Palette("WallColor") }),
                new KeyValuePair<string, Color[]>("корпус/холст", new[] { Palette("CladdingColor"), Palette("Canvas") }),
                new KeyValuePair<string, Color[]>("кристалл", new[] { Palette("CrystalColor"), Palette("CrystalColor") }),
                new KeyValuePair<string, Color[]>("проба", new[] { Palette("SampleColor"), Palette("SampleColor") }),
                new KeyValuePair<string, Color[]>("проба/стенка", new[] { Palette("SampleColor"), Palette("WallColor") }),
            };
            int minSolid = 720, maxDashed = 0, minDashed = 720, maxEmpty = 0, cases = 0;
            foreach (var ground in grounds)
            {
                foreach (double r in new[] { 5.0, 20.0, 47.3, 90.6, 151.0 })
                {
                    foreach (float shift in new[] { 0f, 0.25f, 0.5f, 0.75f })
                    {
                        foreach (int kind in new[] { 0, 1, 2 })
                        {
                            using (Bitmap b = new Bitmap(340, 340))
                            using (Graphics g = Graphics.FromImage(b))
                            {
                                g.SmoothingMode = SmoothingMode.AntiAlias;
                                PointF c = new PointF(170f + shift, 170f + shift);
                                float rf = (float)r;
                                g.Clear(ground.Value[1]);
                                using (Brush inside = new SolidBrush(ground.Value[0]))
                                {
                                    g.FillEllipse(inside, c.X - rf, c.Y - rf, 2f * rf, 2f * rf);
                                }

                                if (kind > 0)
                                {
                                    using (Pen pen = kind == 1 ? new Pen(ink, 1.2f) : (Pen)typeof(GeometrySketch).GetMethod("HiddenPen", Any).Invoke(null, null))
                                    {
                                        g.DrawEllipse(pen, c.X - rf, c.Y - rf, 2f * rf, 2f * rf);
                                    }
                                }

                                int dark = DarkOnRing(b, c, r);
                                cases++;
                                if (kind == 0) maxEmpty = Math.Max(maxEmpty, dark);
                                if (kind == 1) minSolid = Math.Min(minSolid, dark);
                                if (kind == 2) { maxDashed = Math.Max(maxDashed, dark); minDashed = Math.Min(minDashed, dark); }
                                Check(kind == 0 ? dark <= 36 : kind == 1 ? dark >= 684 : dark > 252 && dark < 612,
                                      "контроль меры штриха: " + ground.Key + " r=" + r + " сдвиг " + shift + " " + (kind == 0 ? "пусто" : kind == 1 ? "сплошная" : "штрих") + ": тёмных " + dark);
                            }
                        }
                    }
                }
            }

            Console.WriteLine("контроль меры штриха (синтетика, " + cases + " случаев): сплошная от " + minSolid + ", штрих " + minDashed + "…" + maxDashed + ", пусто до " + maxEmpty + " из 720");
        }

        static void Plans()
        {
            Color wall = Palette("WallColor"), sampleColor = Palette("SampleColor"), canvas = Palette("Canvas"), crystalColor = Palette("CrystalColor"), inkColor = Palette("Ink");
            GeometrySketch s;

            // Маринелли: кольцо пробы и стенка сосуда. (`AMBER324`) Со стороны пробы: проба выше колодца (100 > 70) закрывает
            // колодец и прибор, в центре проба, контуры кристалла и корпуса под ней штриховые.
            GeometryModel mar = Marinelli(NaI());
            mar.MarinelliSideThickness = 8.0;
            mar.MarinelliHoleSideThickness = 6.0;
            GeometrySketch.SketchLabel[] t = Top(mar, out s);
            Has(t, "маринелли", "MarinelliBeakerDiameter", 130.0);
            // (`AMBER324`) ⌀ колодца закрыт пробой и стоит на разрезе: на плане его нет (поручение распорядителя 09.11.2026).
            Check(!t.Any(l => l.Key == "MarinelliHoleDiameter"), "маринелли: на плане ⌀ невидимого колодца");
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                Check(OnCircle(b, c, 0.5 * (44.0 + 57.0) * scale, sampleColor) > 600, "маринелли: проба не в кольце между колодцем и сосудом");
                Check(OnCircle(b, c, (65.0 - 4.0) * scale, wall) > 600, "маринелли: нет стенки сосуда");
                Check(OnCircle(b, c, (38.0 + 3.0) * scale, sampleColor) > 600, "маринелли: стенка колодца не закрыта пробой: вид не со стороны пробы");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), sampleColor), "маринелли: в центре не проба: вид не со стороны пробы");
                double hxM, wrapNaI;
                PlanCrystalOf(s, mar, out hxM, out wrapNaI);
                DashedRing(b, c, hxM * scale, "маринелли: контур кристалла под пробой", true);
                DashedRing(b, c, (hxM + wrapNaI) * scale, "маринелли: контур корпуса под пробой", true);
            }

            // (`AMBER324`) Видна и не видна: банка ⌀40 меньше торца AS 80x80 (сцена постановки `AMBER324`): в центре проба поверх
            // кристалла, на 30 мм от оси — кристалл, не закрытый; его контур на 40 мм сплошной. На «Детекторе» пробы нет вовсе.
            GeometryModel as80 = Preset("Atom Spectra Pro 80x80", GeometryDetectorFacing.Front);
            as80.SourceType = GeometrySourceType.Cylinder;
            as80.BeakerDiameter = 40.0;
            as80.BeakerHeight = 22.0;
            as80.SourceHeight = 20.0;
            as80.BeakerSideWallThickness = 1.0;
            as80.BeakerEndWallThickness = 1.0;
            Top(as80, out s);
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), sampleColor), "банка ⌀40 на AS80: в центре не проба: " + b.GetPixel((int)c.X, (int)c.Y));
                Check(OnCircle(b, c, 30.0 * scale, crystalColor) > 600, "банка ⌀40 на AS80: кристалл вокруг банки закрыт");
                Check(OnCircle(b, c, 10.0 * scale, sampleColor) > 600, "банка ⌀40 на AS80: проба не поверх кристалла");
                double hxA, wrapAs;
                PlanCrystalOf(s, as80, out hxA, out wrapAs);
                DashedRing(b, c, hxA * scale, "банка ⌀40 на AS80: открытый контур кристалла", false);
                // (`AMBER324`) Край кристалла виден, штриха нет — нет и пояснения о нём.
                Check(!Joined(s, s.TopViewBounds).Contains(HiddenHint), "банка ⌀40 на AS80: пояснение о штрихе без штриха");
            }

            GeometrySketch det = Sketch(Cylinder(NaI()), Tab.Detector, new Size(798, 807));
            using (Bitmap b = Paint(det))
            {
                Check(!det.TopViewBounds.IsEmpty && Span(b, det.TopViewBounds, sampleColor).IsEmpty, "«Детектор»: на плане проба");
                PointF c = Field<PointF>(det, "planCenter");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), crystalColor), "«Детектор»: в центре плана не кристалл");
            }

            // Банка: стенка и проба.
            GeometryModel cyl = Cylinder(NaI());
            cyl.BeakerDiameter = 150.0;
            cyl.BeakerSideWallThickness = 8.0;
            t = Top(cyl, out s);
            Check(Joined(s, s.TopViewBounds).Contains(HiddenHint), "банка ⌀150 на NaI: нет пояснения о штрихе под пробой");
            Has(t, "банка", "BeakerDiameter", 150.0);
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                Check(OnCircle(b, c, (75.0 - 4.0) * scale, wall) > 600, "банка: нет стенки");
                Check(OnCircle(b, c, (75.0 - 14.0) * scale, sampleColor) > 600, "банка: нет пробы");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), sampleColor), "банка: в центре не проба: вид не со стороны пробы");
            }

            // Кювета: заливка пробы меряется по X и Y раздельно.
            t = Top(BoxSource(Rc110(GeometryDetectorFacing.Front)), out s);
            Has(t, "кювета", "BoxSourceX", 100.0);
            Has(t, "кювета", "BoxSourceY", 60.0);
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                Rectangle fill = Span(b, s.TopViewBounds, sampleColor);
                Check(Math.Abs(fill.Width - 96.0 * scale) <= Pen, "кювета: проба по X " + fill.Width + " точек против " + Num(96.0 * scale));
                Check(Math.Abs(fill.Height - 56.0 * scale) <= Pen, "кювета: проба по Y " + fill.Height + " точек против " + Num(56.0 * scale));
            }

            // Трапеция: основание 1 при y < 0. (`AMBER324`) Со стороны пробы ось Y вниз: сверху узко (80), снизу широко (100).
            t = Top(Trapezoid(NaI()), out s);
            Has(t, "трапеция", "MarinelliHoleBase1", 80.0);
            Has(t, "трапеция", "MarinelliHoleBase2", 100.0);
            Has(t, "трапеция", "MarinelliHoleWidth", 76.0);
            Check(!t.Any(l => l.Key == "MarinelliHoleDiameter"), "трапеция: на плане диаметр колодца");
            Check(t.Any(l => l.Key == "MarinelliHoleBase1" && l.Bounds.Bottom < s.TopContour.Top), "трапеция: основание 1 не сверху");
            Check(t.Any(l => l.Key == "MarinelliHoleBase2" && l.Bounds.Top > s.TopContour.Bottom - 40f), "трапеция: основание 2 не снизу");

            // По точкам: проба НИЖЕ колодца (60 < 70) — сверху видно донышко колодца цветом стенки, трапецией. Его ширина
            // на 34 мм выше оси (основание 1) уже, чем ниже (основание 2). Кольцо стенки сосуда в обеих строках одно.
            GeometryModel low = Trapezoid(NaI());
            low.MarinelliSourceHeight = 60.0;
            Top(low, out s);
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                int upper = OnRow(b, s.TopViewBounds, (int)(c.Y - 34.0 * scale), wall);
                int lower = OnRow(b, s.TopViewBounds, (int)(c.Y + 34.0 * scale), wall);
                Check(lower > upper + 5.0 * scale, "трапеция перевёрнута: донышко колодца сверху " + upper + ", снизу " + lower + " точек");
                Check(upper > 0, "трапеция: донышка колодца не видно");
            }

            // Точка. (`AMBER324`) Со стороны пробы точка ближе прибора и видна: кружок пробы со сплошным контуром.
            t = Top(Point(Nano(GeometryDetectorFacing.Side)), out s);
            double hx, hy, depth;
            string kx, ky, kd;
            Nano(GeometryDetectorFacing.Side).CrystalBoxInScene(out hx, out hy, out depth, out kx, out ky, out kd);
            Has(t, "точка брусок", kx, 18.0);
            Has(t, "точка брусок", ky, 60.0);
            using (Bitmap b = Paint(s))
            {
                PointF c = Field<PointF>(s, "planCenter");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), sampleColor), "точка: в центре не проба: " + b.GetPixel((int)c.X, (int)c.Y));
                DashedRing(b, c, 5.0, "точка: контур кружка", false);
            }

            // Поле: условная окружность.
            t = Top(Iso(Rc110(GeometryDetectorFacing.Side)), out s);
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                double radius = Field<double>(s, "planRadiusX") * scale;
                Check(OnCircle(b, c, radius, sampleColor) > 100, "поле: нет кольца");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), crystalColor), "поле: прибор закрыт");
            }
        }

        // ------------------------------------------------------------------
        // (12) `AMBER325`: вид со стороны детектора, галка, память выбора
        // ------------------------------------------------------------------

        /// <summary>Сцена постановки `AMBER324`: банка ⌀40 на торце AS 80x80.</summary>
        static GeometryModel As80Cyl40()
        {
            GeometryModel as80 = Preset("Atom Spectra Pro 80x80", GeometryDetectorFacing.Front);
            as80.SourceType = GeometrySourceType.Cylinder;
            as80.BeakerDiameter = 40.0;
            as80.BeakerHeight = 22.0;
            as80.SourceHeight = 20.0;
            as80.BeakerSideWallThickness = 1.0;
            as80.BeakerEndWallThickness = 1.0;
            return as80;
        }

        static string DetectorTitle
        {
            get { return Res("EfficiencySketchDetectorSideView") ?? "View from the detector side"; }
        }

        static Color Pixel(Bitmap b, PointF c, double dx, double dy)
        {
            return b.GetPixel((int)Math.Round(c.X + dx), (int)Math.Round(c.Y + dy));
        }

        /// <summary>
        /// (`AMBER325`) Умолчание «Источника» — вид со стороны детектора: прибор поверх пробы, закрытое им — штрихом, ось Y плана
        /// вверх. На сборке до правки (вид со стороны пробы) каждая сцена обязана отказать.
        /// </summary>
        static void PlansFromDetector()
        {
            Color wall = Palette("WallColor"), sampleColor = Palette("SampleColor"), canvas = Palette("Canvas"), crystalColor = Palette("CrystalColor");
            GeometrySketch s;

            // Банка ⌀40 на AS 80x80: прибор шире банки и закрывает её целиком — в центре кристалл, контур банки штриховой,
            // контур кристалла (открытый) сплошной; пояснение о штрихе есть.
            GeometryModel as80 = As80Cyl40();
            GeometrySketch.SketchLabel[] t = Top(as80, out s, false);
            Check(!FromSample(s), "со стороны детектора: эскиз «Источника» по умолчанию не со стороны детектора");
            Check(Joined(s, s.TopViewBounds).StartsWith(DetectorTitle, StringComparison.Ordinal), "банка ⌀40 на AS80: заголовок плана: " + Joined(s, s.TopViewBounds));
            Check(Joined(s, s.TopViewBounds).Contains(HiddenHint), "банка ⌀40 на AS80 со стороны детектора: нет пояснения о штрихе");
            // (`AMBER325`, второй заход; правило `AMBER324`) Контур банки целиком под прибором — размера на невидимом контуре нет,
            // ⌀40 стоит на разрезе.
            Check(!t.Any(l => l.Key == "BeakerDiameter"), "банка ⌀40 на AS80 со стороны детектора: размер на невидимом контуре");
            Check(s.Labels.Any(l => l.Key == "BeakerDiameter" && s.SideViewBounds.Contains(Rectangle.Round(l.Bounds))), "банка ⌀40 на AS80: ⌀ нет и на разрезе");
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), crystalColor), "банка ⌀40 на AS80 со стороны детектора: в центре не кристалл: " + b.GetPixel((int)c.X, (int)c.Y));
                Check(OnCircle(b, c, 10.0 * scale, crystalColor) > 600, "банка ⌀40 на AS80 со стороны детектора: проба не закрыта прибором");
                Check(OnCircle(b, c, 30.0 * scale, crystalColor) > 600, "банка ⌀40 на AS80 со стороны детектора: кристалл вокруг банки не виден");
                DashedRing(b, c, 20.0 * scale, "банка ⌀40 на AS80 со стороны детектора: контур банки под прибором", true);
                double hxA, wrapAs;
                PlanCrystalOf(s, as80, out hxA, out wrapAs);
                DashedRing(b, c, hxA * scale, "банка ⌀40 на AS80 со стороны детектора: контур кристалла", false);
            }

            // (`AMBER325`, второй заход) Видна часть контура: брусок 60×18 шире банки ⌀40 по X, уже по Y — края банки по X под
            // прибором, по Y видны. ⌀ стоит вертикально, к видимым краям; со стороны пробы — горизонтально, как прежде.
            foreach (bool fromSample in new[] { false, true })
            {
                GeometryModel wide = Preset("Atom Spectra Nano 16", GeometryDetectorFacing.Front);
                wide.CrystalBoxX = 60.0;
                wide.CrystalBoxY = 18.0;
                wide.SourceType = GeometrySourceType.Cylinder;
                wide.BeakerDiameter = 40.0;
                wide.BeakerHeight = 22.0;
                wide.SourceHeight = 20.0;
                wide.BeakerSideWallThickness = 1.0;
                wide.BeakerEndWallThickness = 1.0;
                t = Top(wide, out s, fromSample);
                double hxW, wrapW;
                PlanCrystalOf(s, wide, out hxW, out wrapW);
                Check(hxW + wrapW > 20.0, "брусок 60×18: сцена не та, половина по X " + F(hxW + wrapW));
                GeometrySketch.DimLine[] dims = s.DimensionLines.Where(x => x.OnTopView && x.Key == "BeakerDiameter" && x.Kind == GeometrySketch.DimLineKind.Dimension).ToArray();
                bool vertical = dims.Length > 0 && dims.All(x => x.Side == GeometrySketch.DimSide.Left || x.Side == GeometrySketch.DimSide.Right);
                Check(t.Any(l => l.Key == "BeakerDiameter"), "банка ⌀40 на бруске 60×18" + (fromSample ? " со стороны пробы" : "") + ": на плане нет ⌀");
                Check(vertical == !fromSample, "банка ⌀40 на бруске 60×18" + (fromSample ? " со стороны пробы" : " со стороны детектора") + ": ⌀ " + (vertical ? "вертикально" : "горизонтально"));
            }

            // Сцена постановки `AMBER325`: прямоугольная банка 40×40 на RC-103. Банка шире прибора: в центре кристалл, проба видна
            // вокруг прибора, контур банки сплошной, штриха нет — нет и пояснения о нём.
            GeometryModel rc103 = Preset("RadiaCode-103", GeometryDetectorFacing.Front);
            rc103.SourceType = GeometrySourceType.Box;
            rc103.BoxSourceX = 40.0;
            rc103.BoxSourceY = 40.0;
            rc103.BoxSourceHeight = 20.0;
            rc103.BoxSideWallThickness = 1.0;
            rc103.BoxEndWallThickness = 1.0;
            t = Top(rc103, out s, false);
            Has(t, "банка 40×40 на RC-103", "BoxSourceX", 40.0);
            Has(t, "банка 40×40 на RC-103", "BoxSourceY", 40.0);
            Check(!Joined(s, s.TopViewBounds).Contains(HiddenHint), "банка 40×40 на RC-103 со стороны детектора: пояснение о штрихе без штриха");
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), crystalColor), "банка 40×40 на RC-103 со стороны детектора: в центре не кристалл: " + b.GetPixel((int)c.X, (int)c.Y));
                Check(Near(Pixel(b, c, 15.0 * scale, 15.0 * scale), sampleColor), "банка 40×40 на RC-103: проба вокруг прибора не видна");
                Rectangle fill = Span(b, s.TopViewBounds, sampleColor);
                Check(Math.Abs(fill.Width - 38.0 * scale) <= Pen && Math.Abs(fill.Height - 38.0 * scale) <= Pen,
                      "банка 40×40 на RC-103: проба " + fill.Width + "×" + fill.Height + " точек против " + Num(38.0 * scale));
            }

            // Маринелли на NaI: колодец шире прибора — проба в кольце видна, в центре кристалл, контур колодца виден мимо прибора и
            // сплошной; штриха нет — нет и пояснения.
            GeometryModel mar = Marinelli(NaI());
            mar.MarinelliSideThickness = 8.0;
            mar.MarinelliHoleSideThickness = 6.0;
            t = Top(mar, out s, false);
            Has(t, "маринелли со стороны детектора", "MarinelliBeakerDiameter", 130.0);
            Check(!t.Any(l => l.Key == "MarinelliHoleDiameter"), "маринелли со стороны детектора: ⌀ колодца стоит дважды — на разрезе и на плане");
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                double hxM, wrapNaI;
                PlanCrystalOf(s, mar, out hxM, out wrapNaI);
                Check(hxM + wrapNaI < 37.0, "маринелли: прибор NaI шире колодца — сцена не та: " + F(hxM + wrapNaI));
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), crystalColor), "маринелли со стороны детектора: в центре не кристалл: " + b.GetPixel((int)c.X, (int)c.Y));
                Check(OnCircle(b, c, 0.5 * (44.0 + 57.0) * scale, sampleColor) > 600, "маринелли со стороны детектора: проба в кольце не видна");
                Check(OnCircle(b, c, (65.0 - 4.0) * scale, wall) > 600, "маринелли со стороны детектора: нет стенки сосуда");
                DashedRing(b, c, 38.0 * scale, "маринелли со стороны детектора: контур колодца мимо прибора", false);
                DashedRing(b, c, (hxM + wrapNaI) * scale, "маринелли со стороны детектора: контур корпуса", false);
                Check(!Joined(s, s.TopViewBounds).Contains(HiddenHint), "маринелли на NaI со стороны детектора: пояснение о штрихе без штриха");
            }

            // Маринелли с колодцем уже прибора (⌀40 на AS 80x80, сцена нарочная): колодец под прибором — штриховой, пояснение есть.
            GeometryModel narrow = Marinelli(Preset("Atom Spectra Pro 80x80", GeometryDetectorFacing.Front));
            narrow.MarinelliHoleDiameter = 40.0;
            Top(narrow, out s, false);
            Check(Joined(s, s.TopViewBounds).Contains(HiddenHint), "маринелли ⌀40 под AS80 со стороны детектора: нет пояснения о штрихе");
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), crystalColor), "маринелли ⌀40 под AS80: в центре не кристалл");
                DashedRing(b, c, 20.0 * scale, "маринелли ⌀40 под AS80 со стороны детектора: контур колодца под прибором", true);
            }

            // Трапеция: со стороны детектора ось Y вверх, основание 2 (100, y > 0) сверху, основание 1 (80) снизу — как до `AMBER324`.
            t = Top(Trapezoid(NaI()), out s, false);
            Has(t, "трапеция со стороны детектора", "MarinelliHoleBase1", 80.0);
            Has(t, "трапеция со стороны детектора", "MarinelliHoleBase2", 100.0);
            Has(t, "трапеция со стороны детектора", "MarinelliHoleWidth", 76.0);
            Check(t.Any(l => l.Key == "MarinelliHoleBase2" && l.Bounds.Bottom < s.TopContour.Top), "трапеция со стороны детектора: основание 2 не сверху");
            Check(t.Any(l => l.Key == "MarinelliHoleBase1" && l.Bounds.Top > s.TopContour.Bottom - 40f), "трапеция со стороны детектора: основание 1 не снизу");
            using (Bitmap b = Paint(s))
            {
                // По точкам: на 37 мм выше оси колодец широк (половина ≈ 49.9) — точка x = 48 в просвете колодца; на 37 мм ниже
                // оси колодец узок (≈ 40.1 и стенка 2) — та же x в пробе. Прибор NaI до них не достаёт.
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                Color upper = Pixel(b, c, 48.0 * scale, -37.0 * scale), lower = Pixel(b, c, 48.0 * scale, 37.0 * scale);
                Check(Near(upper, canvas) && Near(lower, sampleColor), "трапеция со стороны детектора перевёрнута: выше оси " + upper + ", ниже " + lower);
            }

            // Точка: со стороны детектора прибор её закрывает — в центре кристалл, кружок штриховой.
            Top(Point(Nano(GeometryDetectorFacing.Side)), out s, false);
            using (Bitmap b = Paint(s))
            {
                PointF c = Field<PointF>(s, "planCenter");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), crystalColor), "точка со стороны детектора: в центре не кристалл: " + b.GetPixel((int)c.X, (int)c.Y));
                DashedRing(b, c, 5.0, "точка со стороны детектора: кружок за прибором", true);
            }

            // Поле: прибор не закрыт и ничего не закрывает.
            Top(Iso(Rc110(GeometryDetectorFacing.Side)), out s, false);
            Check(!Joined(s, s.TopViewBounds).Contains(HiddenHint), "поле со стороны детектора: пояснение о штрихе");
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                double radius = Field<double>(s, "planRadiusX") * scale;
                Check(OnCircle(b, c, radius, sampleColor) > 100, "поле со стороны детектора: нет кольца");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), crystalColor), "поле со стороны детектора: прибор закрыт");
            }

            // Банка ⌀150 на NaI: банка шире прибора — проба и стенка видны, в центре кристалл, штриха нет.
            GeometryModel cyl = Cylinder(NaI());
            cyl.BeakerDiameter = 150.0;
            cyl.BeakerSideWallThickness = 8.0;
            Top(cyl, out s, false);
            Check(!Joined(s, s.TopViewBounds).Contains(HiddenHint), "банка ⌀150 на NaI со стороны детектора: пояснение о штрихе без штриха");
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                Check(OnCircle(b, c, (75.0 - 4.0) * scale, wall) > 600, "банка ⌀150 со стороны детектора: нет стенки");
                Check(OnCircle(b, c, (75.0 - 14.0) * scale, sampleColor) > 600, "банка ⌀150 со стороны детектора: нет пробы");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), crystalColor), "банка ⌀150 со стороны детектора: в центре не кристалл");
            }

            // Кювета: заливка пробы по X и Y — те же размеры, что со стороны пробы.
            t = Top(BoxSource(Rc110(GeometryDetectorFacing.Front)), out s, false);
            Has(t, "кювета со стороны детектора", "BoxSourceX", 100.0);
            Has(t, "кювета со стороны детектора", "BoxSourceY", 60.0);
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                Rectangle fill = Span(b, s.TopViewBounds, sampleColor);
                Check(Math.Abs(fill.Width - 96.0 * scale) <= Pen && Math.Abs(fill.Height - 56.0 * scale) <= Pen,
                      "кювета со стороны детектора: проба " + fill.Width + "×" + fill.Height + " точек");
            }
        }

        /// <summary>
        /// (`AMBER325`) Галка: на «Источнике» — у заголовка второго вида, в его рамке, в положении выбора, с подписью языка; на
        /// «Детекторе» и «Защите» — нет. Переключение — только рисунок: модель не меняется.
        /// </summary>
        static void SideCheck()
        {
            foreach (string culture in new[] { "en", "ru" })
            {
                Resources.Culture = new CultureInfo(culture);
                string text = Res("EfficiencySketchFromSampleCheck") ?? "?";
                foreach (Size size in FieldSizes.Concat(MoreSizes))
                {
                    foreach (bool fromSample in new[] { false, true })
                    {
                        string where = culture + " галка «Источник»" + (fromSample ? " (со стороны пробы) " : " ") + size.Width + "×" + size.Height + ": ";
                        GeometrySketch s = Sketch(As80Cyl40(), Tab.Source, size, fromSample);
                        using (Paint(s))
                        {
                            Rectangle r = CheckBounds(s), plan = s.TopViewBounds;
                            Check(r.IsEmpty == plan.IsEmpty, where + "галка " + (r.IsEmpty ? "не поставлена" : "без плана"));
                            if (r.IsEmpty)
                            {
                                continue;
                            }

                            Check(plan.Contains(r), where + "галка вне рамки плана: " + r + " в " + plan);
                            // (`AMBER325`, второй заход) Галка — в строке заголовка, за его последней строкой: своей строки у неё
                            // нет, высоты заголовку она не прибавляет. С подписью или одним квадратом (узкий план).
                            // (`AMBER325`, четвёртый заход) Рисунок галки (квадрат и подпись) — подпись чертежа с её именем внутри
                            // рамки галки; рамка самой галки по `PreferredSize` может быть выше строки.
                            RectangleF rf = r;
                            GeometrySketch.SketchLabel ink = In(s, plan).FirstOrDefault(l => l.Text == text && l.Bounds.Left >= rf.Left - 0.01f
                                && l.Bounds.Right <= rf.Right + 0.01f && l.Bounds.Top >= rf.Top - 0.01f && l.Bounds.Bottom <= rf.Bottom + 0.01f);
                            Check(ink.Text != null, where + "нет подписи-рисунка галки в её рамке");
                            GeometrySketch.SketchLabel[] rest = In(s, plan).Where(l => !(l.Text == ink.Text && l.Bounds == ink.Bounds)).OrderBy(l => l.Bounds.Top).ToArray();
                            string title = fromSample ? Res("EfficiencySketchTopView") : DetectorTitle;
                            int n = Enumerable.Range(1, rest.Length).FirstOrDefault(k => string.Join(" ", rest.Take(k).Select(l => l.Text)) == title);
                            Check(n > 0, where + "нет заголовка «" + title + "»");
                            if (n > 0)
                            {
                                RectangleF last = rest[n - 1].Bounds;
                                RectangleF drawn = ink.Text != null ? ink.Bounds : rf;
                                Check(drawn.Top >= last.Top - 0.01f && drawn.Bottom <= last.Bottom + 0.01f && r.Left >= last.Right
                                      && Math.Abs((rf.Top + rf.Bottom) / 2f - (last.Top + last.Bottom) / 2f) <= 1.01f,
                                      where + "галка не в строке заголовка: " + r + " (рисунок " + drawn + ") при строке " + last);
                            }

                            // (`AMBER325`, третий заход) Amber 11.11.2026 вопросником, дословно: «Короткая подпись в узком
                            // (Рекомендую)». Вид галки — по мерке, независимой от чертежа: полная подпись, если заголовок в одну
                            // строку и всё помещается в ширину плана; иначе короткая, если помещается она; иначе один квадрат.
                            CheckBox box = s.Controls.OfType<CheckBox>().FirstOrDefault();
                            string shortText = Res("EfficiencySketchFromSampleShort") ?? (culture == "ru" ? "от пробы" : "from sample");
                            string expected = "квадрат";
                            if (n > 0)
                            {
                                RectangleF last = rest[n - 1].Bounds;
                                using (Bitmap mb = new Bitmap(4, 4))
                                using (Graphics mg = Graphics.FromImage(mb))
                                {
                                    float head = 8f + last.Width + 10f, room = plan.Width - 8f;
                                    float fullW = (float)Math.Ceiling(mg.MeasureString(text, s.Font).Width + 24f);
                                    float shortW = (float)Math.Ceiling(mg.MeasureString(shortText, s.Font).Width + 24f);
                                    expected = n == 1 && head + fullW <= room ? "полная" : head + shortW <= room ? "короткая" : "квадрат";
                                }
                            }

                            string kind = box == null ? "нет" : box.Text == "" ? "квадрат" : box.Text == text ? "полная" : box.Text == shortText ? "короткая" : "«" + box.Text + "»";
                            Check(kind == expected, where + "вид галки " + kind + ", по мерке — " + expected);
                            Check(box != null && box.Bounds == r && box.AccessibleName == text && box.Checked == fromSample,
                                  where + "галка не та: " + (box == null ? "нет" : box.Bounds + " «" + box.Text + "» " + box.AccessibleName + " " + box.Checked));
                            Check(r.Right <= plan.Right - 2, where + "галка у края рамки плана: " + r + " в " + plan);
                            // (`AMBER325`, четвёртый заход, вердикт `crit325`) Подпись галки не срезана: рамка не меньше
                            // `PreferredSize`, и рисунок галки в её рамке тот же, что в рамке на 8 точек выше (хвосты «р», «p»).
                            if (box != null)
                            {
                                Size pref = box.PreferredSize;
                                Check(box.Height >= pref.Height && box.Width >= pref.Width, where + "рамка галки " + box.Size + " меньше PreferredSize " + pref);
                                int inFrame = CheckInk(box, box.Size), inTall = CheckInk(box, new Size(box.Width, box.Height + 8));
                                ClipSeen++;
                                Check(inFrame == inTall, where + "подпись галки срезана: тёмных " + inFrame + " в рамке, " + inTall + " в рамке на 8 выше");
                            }
                            // Подпись помещается — она есть: в настоящем окне 798×807 план рядом, ширина ~400.
                            if (size.Width >= 798)
                            {
                                Check(kind == "полная", where + "в окне " + size.Width + " галка не с полной подписью");
                            }

                            string bucket = culture + " " + kind;
                            int seenKind;
                            KindSeen.TryGetValue(bucket, out seenKind);
                            KindSeen[bucket] = seenKind + 1;
                        }
                    }

                    foreach (Tab tab in new[] { Tab.Detector, Tab.Shield })
                    {
                        GeometrySketch d = Sketch(tab == Tab.Shield ? Shielded(As80Cyl40()) : As80Cyl40(), tab, size);
                        using (Paint(d))
                        {
                            Check(CheckBounds(d).IsEmpty && !d.Controls.OfType<CheckBox>().Any(c => c.Visible),
                                  culture + " " + tab + " " + size.Width + "×" + size.Height + ": галка на вкладке без выбора стороны");
                            Check(!string.Join(" ", d.Labels.Select(l => l.Text)).Contains(text), culture + " " + tab + " " + size.Width + ": подпись галки");
                        }
                    }
                }
            }

            Resources.Culture = null;
        }

        static readonly Dictionary<string, int> KindSeen = new Dictionary<string, int>();

        static int ClipSeen;

        /// <summary>
        /// (`AMBER325`, четвёртый заход) Сколько тёмных точек (сумма RGB &lt; 300) у галки той же подписи, шрифта и положения,
        /// нарисованной своим `DrawToBitmap` в рамке заданного размера на сплошном фоне чертежа. Мера критика `crit325`
        /// (`ctrl\CheckClip.cs`): срезанный низ букв даёт меньше точек, чем рамка на 8 точек выше.
        /// </summary>
        static int CheckInk(CheckBox like, Size size)
        {
            using (CheckBox c = new CheckBox { AutoSize = false, Text = like.Text, Font = like.Font, Checked = like.Checked, ForeColor = like.ForeColor,
                                               BackColor = Palette("Canvas"), UseVisualStyleBackColor = false, Size = size })
            using (Bitmap b = new Bitmap(size.Width, size.Height))
            {
                c.DrawToBitmap(b, new Rectangle(System.Drawing.Point.Empty, size));
                int dark = 0;
                for (int y = 0; y < b.Height; y++)
                {
                    for (int x = 0; x < b.Width; x++)
                    {
                        Color p = b.GetPixel(x, y);
                        if (p.R + p.G + p.B < 300)
                        {
                            dark++;
                        }
                    }
                }

                return dark;
            }
        }

        static string configCopy, configOriginal;

        static object Manager()
        {
            return typeof(GlobalConfigManager).GetField("instance", Any).GetValue(null);
        }

        /// <summary>(`AMBER325`) Главный конфиг пробы — во временную копию: галка пишет его, а каталог проб общий.</summary>
        static void ConfigCopy()
        {
            object manager = Manager();
            FieldInfo path = typeof(GlobalConfigManager).GetField("becqMoniMainConfig", Any);
            configOriginal = (string)path.GetValue(manager);
            configCopy = Path.Combine(Path.GetTempPath(), "a307_config_" + Guid.NewGuid().ToString("N") + ".xml");
            if (File.Exists(configOriginal))
            {
                File.Copy(configOriginal, configCopy);
            }
            else
            {
                using (FileStream f = File.Create(configCopy))
                {
                    new XmlSerializer(typeof(GlobalConfigInfo)).Serialize(f, new GlobalConfigInfo());
                }
            }

            path.SetValue(manager, configCopy);
            typeof(GlobalConfigManager).GetField("isLoaded", Any).SetValue(manager, false);
        }

        static void ConfigCopyDrop()
        {
            object manager = Manager();
            typeof(GlobalConfigManager).GetField("becqMoniMainConfig", Any).SetValue(manager, configOriginal);
            typeof(GlobalConfigManager).GetField("isLoaded", Any).SetValue(manager, false);
            if (configCopy != null && File.Exists(configCopy))
            {
                File.Delete(configCopy);
            }
        }

        /// <summary>«Следующее открытие»: конфиг перечитывается с диска тем же путём, что на запуске программы.</summary>
        static GlobalConfigInfo Reload()
        {
            typeof(GlobalConfigManager).GetField("isLoaded", Any).SetValue(Manager(), false);
            return GlobalConfigManager.GetInstance().GlobalConfig;
        }

        /// <summary>
        /// (`AMBER325`) Память выбора: старый конфиг без поля — со стороны детектора; галка пишет выбор в главный конфиг и на диск;
        /// новый эскиз и перечитанный с диска конфиг дают тот же выбор; присваивание из кода в конфиг не пишет; модель не меняется.
        /// </summary>
        static void SideMemory()
        {
            PropertyInfo field = typeof(GlobalConfigInfo).GetProperty("GeometrySketchPlanFromSample");
            Check(field != null, "память: в главном конфиге нет поля GeometrySketchPlanFromSample");
            XmlSerializer xs = new XmlSerializer(typeof(GlobalConfigInfo));
            if (File.Exists(configOriginal))
            {
                string original = File.ReadAllText(configOriginal);
                Check(!original.Contains("GeometrySketchPlanFromSample"), "память: в конфиге каталога проб уже есть поле — старый конфиг не проверен");
                using (StringReader r = new StringReader(original))
                {
                    GlobalConfigInfo old = (GlobalConfigInfo)xs.Deserialize(r);
                    Check(field != null && !(bool)field.GetValue(old), "память: старый конфиг без поля читается не «со стороны детектора»");
                }
            }

            using (StringReader r = new StringReader("<GlobalConfigInfo />"))
            {
                GlobalConfigInfo bare = (GlobalConfigInfo)xs.Deserialize(r);
                Check(field != null && !(bool)field.GetValue(bare), "память: пустой конфиг читается не «со стороны детектора»");
            }

            if (field == null)
            {
                return;
            }

            GlobalConfigInfo config = Reload();
            Check(!(bool)field.GetValue(config), "память: копия конфига каталога проб уже «со стороны пробы»");
            GeometryModel m = As80Cyl40();
            string before = Xml(m);
            GeometrySketch s = Sketch(m, Tab.Source, new Size(798, 807));
            using (Paint(s))
            {
            }

            Check(!FromSample(s), "память: новый эскиз не со стороны детектора");
            CheckBox box = s.Controls.OfType<CheckBox>().FirstOrDefault();
            Check(box != null && !box.Checked, "память: галки нет или она стоит");
            if (box == null)
            {
                return;
            }

            // Присваивание из кода — только рисунок, конфиг не трогается.
            SetFromSample(s, true);
            Check(!(bool)field.GetValue(GlobalConfigManager.GetInstance().GlobalConfig), "память: присваивание из кода записало конфиг");
            SetFromSample(s, false);

            // Человек ставит галку.
            box.Checked = true;
            Check(FromSample(s), "память: галка не переключила вид");
            Check((bool)field.GetValue(GlobalConfigManager.GetInstance().GlobalConfig), "память: галка не записала выбор в главный конфиг");
            string saved = File.Exists(configCopy) ? File.ReadAllText(configCopy) : "";
            Check(saved.Contains("<GeometrySketchPlanFromSample>true</GeometrySketchPlanFromSample>"), "память: выбор не сохранён на диск");
            using (Bitmap b = Paint(s))
            {
                PointF c = Field<PointF>(s, "planCenter");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), Palette("SampleColor")), "память: после галки вид не со стороны пробы");
                Check(Joined(s, s.TopViewBounds).StartsWith(Res("EfficiencySketchTopView"), StringComparison.Ordinal), "память: после галки заголовок не «со стороны пробы»");
            }

            Check(Xml(m) == before, "память: переключение галки изменило модель");

            GeometrySketch again = Sketch(As80Cyl40(), Tab.Source, new Size(798, 807));
            Check(FromSample(again), "память: новый эскиз в том же сеансе забыл выбор");
            Check((bool)field.GetValue(Reload()), "память: перечитанный с диска конфиг забыл выбор");
            GeometrySketch next = Sketch(As80Cyl40(), Tab.Source, new Size(798, 807));
            using (Paint(next))
            {
            }

            Check(FromSample(next), "память: эскиз после перечтения конфига забыл выбор");
            CheckBox nextBox = next.Controls.OfType<CheckBox>().FirstOrDefault();
            Check(nextBox != null && nextBox.Checked, "память: галка после перечтения конфига снята");

            // И обратно.
            if (nextBox != null)
            {
                nextBox.Checked = false;
            }

            Check(!(bool)field.GetValue(Reload()), "память: снятая галка не записалась на диск");
            GeometrySketch last = Sketch(As80Cyl40(), Tab.Source, new Size(798, 807));
            Check(!FromSample(last), "память: после снятия галки эскиз не со стороны детектора");
        }

        // ------------------------------------------------------------------
        // (8) Миниатюра, (9) модель, (10) случайная сцена
        // ------------------------------------------------------------------

        static void Overview()
        {
            foreach (Size size in new[] { new Size(300, 200), new Size(220, 120), new Size(500, 500) })
            {
                GeometrySketch s = new GeometrySketch { Mode = GeometrySketch.SketchMode.Overview, Size = size };
                s.SetModel(Marinelli(NaI()));
                using (Paint(s))
                {
                    Check(s.SideViewBounds.IsEmpty && s.TopViewBounds.IsEmpty, "миниатюра " + size.Width + ": появились два вида");
                    Check(s.DimensionLines.Length == 0, "миниатюра " + size.Width + ": появились размеры");
                }
            }
        }

        static string Xml(GeometryModel m)
        {
            XmlSerializer x = new XmlSerializer(typeof(GeometryModel));
            using (StringWriter w = new StringWriter(CultureInfo.InvariantCulture))
            {
                x.Serialize(w, m);
                return w.ToString();
            }
        }

        static void ModelUntouched()
        {
            foreach (var crystal in Crystals)
            {
                foreach (var source in Sources)
                {
                    GeometryModel m = source.Value(crystal.Value());
                    string before = Xml(m);
                    foreach (Size size in FieldSizes)
                    {
                        GeometrySketch s = Sketch(m, Tab.Source, size);
                        using (Paint(s))
                        {
                        }
                    }

                    Check(before == Xml(m), crystal.Key + " + " + source.Key + ": отрисовка изменила модель");
                }
            }
        }

        /// <summary>Случайная модель критика: без рамки вида «0.07» уходило за разрез.</summary>
        static void CriticScene()
        {
            GeometryModel g = GeometryEditorPanel.Blank();
            g.Shape = CrystalShape.Box;
            g.CrystalBoxX = 6.69;
            g.CrystalBoxY = 39.48;
            g.CrystalBoxZ = 140.09;
            g.Facing = GeometryDetectorFacing.Side;
            g.FrontReflectorThickness = 0.32;
            g.SideReflectorThickness = 0.39;
            g.FrontGapThickness = 0.07;
            g.SideGapThickness = 0.79;
            g.FrontCladdingThickness = 0.44;
            g.SideCladdingThickness = 0.73;
            g.MountingThickness = 0.15;
            Shielded(Marinelli(g));
            foreach (Tab tab in new[] { Tab.Detector, Tab.Source, Tab.Shield })
            {
                foreach (Size size in FieldSizes)
                {
                    GeometrySketch s = Sketch(g, tab, size);
                    using (Paint(s))
                    {
                        Rectangle a = s.SideViewBounds, b = s.TopViewBounds;
                        string where = "сцена критика " + tab + " " + size.Width + ": ";
                        foreach (string line in Gost(LayoutOf(s)).Take(3))
                        {
                            Check(false, where + line);
                        }

                        foreach (GeometrySketch.SketchLabel label in s.Labels)
                        {
                            if (!a.IsEmpty)
                            {
                                RectangleF ra = RectangleF.Intersect(label.Bounds, a), rb = RectangleF.Intersect(label.Bounds, b);
                                Check(Whole(ra, label.Bounds) || Whole(rb, label.Bounds), where + "подпись пересекает рамку вида: " + label.Text + " y=" + Num(label.Bounds.Y));
                            }
                        }

                        foreach (GeometrySketch.DimLine line in s.DimensionLines)
                        {
                            if (!a.IsEmpty)
                            {
                                Rectangle own = line.OnTopView ? b : a;
                                Check(own.Contains(System.Drawing.Point.Round(line.A)) && own.Contains(System.Drawing.Point.Round(line.B)), where + "линия вышла за рамку: " + line.Key);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Amber 06.11.2026: «А здесь почему не так сделали? В ГОСТ иначе написано?» Тонкая обвязка сбоку и боковая стенка сосуда
        /// стоят на уровне тела, числа на свободной стороне, короткой линией, без выносных вдоль чертежа.
        /// </summary>
        static void ShortRoutes()
        {
            foreach (var crystal in Crystals)
            {
                foreach (Size size in FieldSizes)
                {
                    GeometrySketch s = Sketch(crystal.Value(), Tab.Detector, size);
                    using (Paint(s))
                    {
                        string where = crystal.Key + " " + size.Width + ": ";
                        bool rotated = crystal.Key.EndsWith("бок", StringComparison.Ordinal);
                        string[] keys = rotated
                            ? new[] { "FrontReflectorThickness", "FrontGapThickness", "FrontCladdingThickness" }
                            : new[] { "SideReflectorThickness", "SideGapThickness", "SideCladdingThickness" };
                        RectangleF c = s.SideContour;
                        foreach (GeometrySketch.DimLine line in s.DimensionLines.Where(x => keys.Contains(x.Key)))
                        {
                            Check(line.Kind != GeometrySketch.DimLineKind.Extension, where + "у боковой обвязки выносная вдоль чертежа: " + line.Key);
                            Check(line.Side == GeometrySketch.DimSide.Left, where + "боковая обвязка не слева: " + line.Key);
                        }

                        foreach (GeometrySketch.SketchLabel label in s.Labels.Where(x => keys.Contains(x.Key)))
                        {
                            Check(label.Bounds.Right <= c.Left, where + "число боковой обвязки не на свободной стороне: " + label.Text);
                        }
                    }
                }
            }

            GeometrySketch.DimSide free = GeometrySketch.DimSide.Left;
            foreach (KeyValuePair<string, GeometryModel> scene in new[]
            {
                new KeyValuePair<string, GeometryModel>("MarinelliSideThickness", Marinelli(NaI())),
                new KeyValuePair<string, GeometryModel>("BeakerSideWallThickness", Cylinder(NaI())),
                new KeyValuePair<string, GeometryModel>("BoxSideWallThickness", BoxSource(NaI())),
            })
            {
                GeometrySketch s = Sketch(scene.Value, Tab.Source, new Size(798, 807));
                using (Paint(s))
                {
                    GeometrySketch.DimLine[] mine = s.DimensionLines.Where(x => x.Key == scene.Key).ToArray();
                    Check(mine.Length > 0 && mine.All(x => x.Kind != GeometrySketch.DimLineKind.Extension && x.Side == free), scene.Key + ": стенка не короткой линией на свободной стороне");
                }
            }
        }

        // ------------------------------------------------------------------
        // Сверка с картинкой: что записано, то и напечатано
        // ------------------------------------------------------------------

        static readonly Color NumberInk = Color.FromArgb(0xFF, 0x00, 0xFF);

        static bool MagentaBlend(Color c)
        {
            return c.R - c.G >= 60 && c.B - c.G >= 60;
        }

        /// <summary>Число в своей рамке и нигде больше; в рамке нет чужой краски; поворот виден по чернилам; п. 5.21, 5.22, 5.38.</summary>
        static void Picture(GeometrySketch s, string where, Color canvas)
        {
            s.DimensionNumberColor = NumberInk;
            using (Bitmap b = Paint(s))
            {
                KeyValuePair<string, RectangleF>[] numbers = s.DimensionNumbers;
                bool[,] mine = new bool[b.Width, b.Height];

                // Прочие подписи (заголовки, таблички) печатаются своим чёрным: ClearType даёт им цветную кайму.
                foreach (GeometrySketch.SketchLabel label in s.Labels)
                {
                    if (!numbers.Any(x => x.Value == label.Bounds))
                    {
                        for (int y = Math.Max(0, (int)label.Bounds.Top - 1); y < Math.Min(b.Height, (int)label.Bounds.Bottom + 2); y++)
                        {
                            for (int x = Math.Max(0, (int)label.Bounds.Left - 1); x < Math.Min(b.Width, (int)label.Bounds.Right + 2); x++)
                            {
                                mine[x, y] = true;
                            }
                        }
                    }
                }

                foreach (KeyValuePair<string, RectangleF> n in numbers)
                {
                    RectangleF r = n.Value;
                    int x0 = Math.Max(0, (int)Math.Floor(r.Left)), y0 = Math.Max(0, (int)Math.Floor(r.Top));
                    int x1 = Math.Min(b.Width - 1, (int)Math.Ceiling(r.Right)), y1 = Math.Min(b.Height - 1, (int)Math.Ceiling(r.Bottom));
                    int ink = 0, foreign = 0, ix0 = int.MaxValue, iy0 = int.MaxValue, ix1 = -1, iy1 = -1;
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            mine[x, y] = true;
                            Color c = b.GetPixel(x, y);
                            if (MagentaBlend(c))
                            {
                                ink++;
                                ix0 = Math.Min(ix0, x);
                                iy0 = Math.Min(iy0, y);
                                ix1 = Math.Max(ix1, x);
                                iy1 = Math.Max(iy1, y);
                            }
                            else if (c.G >= c.R - 10 && c.G >= c.B - 30 && !Near(c, canvas, 50) && x > x0 + 1 && x < x1 - 1 && y > y0 + 1 && y < y1 - 1)
                            {
                                foreign++;
                            }
                        }
                    }

                    Check(ink > 0, where + ": число «" + n.Key + "» не напечатано в своей рамке");
                    Check(foreign <= 2, where + ": в рамке числа «" + n.Key + "» чужая краска, точек " + foreign);
                    if (ink > 0 && n.Key.Length >= 3)
                    {
                        bool turned = r.Height > r.Width;
                        Check(turned == (iy1 - iy0 > ix1 - ix0), where + ": поворот числа «" + n.Key + "» на картинке не тот");
                    }
                }

                int stray = 0;
                for (int y = 0; y < b.Height; y++)
                {
                    for (int x = 0; x < b.Width; x++)
                    {
                        if (!mine[x, y] && MagentaBlend(b.GetPixel(x, y)))
                        {
                            stray++;
                        }
                    }
                }

                Check(stray == 0, where + ": число вне записанной рамки, точек " + stray);
            }

            s.DimensionNumberColor = Palette("Ink");

            // П. 5.21 и 5.22: окончания по ширине размера, как нарисованы.
            foreach (GeometrySketch.DimEnd end in s.DimensionEnds)
            {
                float width = end.SpanB - end.SpanA;
                if (end.InChain && width < 12f)
                {
                    Check(end.Points.Length == 2, where + ": у тесного звена цепочки не засечка: " + end.Text);
                }

                if (end.Points.Length == 2)
                {
                    float dx = Math.Abs(end.Points[0].X - end.Points[1].X), dy = Math.Abs(end.Points[0].Y - end.Points[1].Y);
                    Check(Math.Abs(dx - dy) < 0.01f, where + ": засечка не под 45°: " + end.Text);
                }
                else if (!end.InChain)
                {
                    bool h = Math.Abs(end.Points[1].X - end.Points[2].X) < 0.01f;
                    float tip = h ? end.Points[0].X : end.Points[0].Y, feather = h ? end.Points[1].X : end.Points[1].Y;
                    bool inward = (feather > tip) == (Math.Abs(tip - end.SpanA) < 0.01f);
                    Check(inward == (width >= 12f), where + ": стрелки «" + end.Text + "» шириной " + Num(width) + " не на той стороне");
                }
            }

            // П. 5.38: диаметр со знаком.
            foreach (GeometrySketch.SketchLabel label in s.Labels)
            {
                if (label.Key != null && label.Key.EndsWith("Diameter", StringComparison.Ordinal) && s.DimensionNumbers.Any(x => x.Key == label.Text))
                {
                    Check(label.Text.StartsWith("⌀", StringComparison.Ordinal), where + ": диаметр без знака: " + label.Text);
                }
            }
        }

        /// <summary>Выносная тоньше контура и светлее него: на картинке, поперёк линии.</summary>
        static void GuideLines()
        {
            GeometrySketch s = Sketch(Marinelli(Rc110(GeometryDetectorFacing.Side)), Tab.Source, new Size(798, 807));
            using (Bitmap b = Paint(s))
            {
                Color canvas = Palette("Canvas");
                int measured = 0;
                foreach (GeometrySketch.DimLine line in s.DimensionLines.Where(x => x.Kind == GeometrySketch.DimLineKind.Extension && !x.OnTopView))
                {
                    RectangleF c = s.SideContour;
                    bool horizontal = Math.Abs(line.A.Y - line.B.Y) < 0.5f;
                    PointF mid = new PointF(line.B.X + (horizontal ? (line.B.X < line.A.X ? 6f : -6f) : 0f), line.B.Y + (horizontal ? 0f : (line.B.Y < line.A.Y ? 6f : -6f)));
                    if (c.Contains(mid))
                    {
                        continue;
                    }

                    // Пара линий ближе 6 точек мерится не как одна линия: это две выносные тонкой стенки.
                    float own = horizontal ? line.A.Y : line.A.X;
                    if (s.DimensionLines.Any(x => x.Kind == GeometrySketch.DimLineKind.Extension && !x.OnTopView && x.Side == line.Side
                                                  && Math.Abs((horizontal ? x.A.Y : x.A.X) - own) > 0.5f && Math.Abs((horizontal ? x.A.Y : x.A.X) - own) < 6f))
                    {
                        continue;
                    }

                    // Поперёк линии от её середины в обе стороны, пока не кончится краска: соседние линии не в счёт.
                    Func<int, Color> across = k => horizontal ? b.GetPixel((int)mid.X, (int)Math.Floor(mid.Y) + k) : b.GetPixel((int)Math.Floor(mid.X) + k, (int)mid.Y);
                    int wide = 0, darkest = 999;
                    for (int step = -1; step <= 1; step += 2)
                    {
                        for (int k = step < 0 ? 0 : 1; Math.Abs(k) <= 6 && !Near(across(k), canvas, 24); k += step)
                        {
                            Color p = across(k);
                            wide++;
                            darkest = Math.Min(darkest, p.R + p.G + p.B);
                        }
                    }

                    if (wide > 0)
                    {
                        measured++;
                        Check(wide <= 2, "выносная шире 2 точек: " + wide + ", " + line.Key);
                        Color ink = Palette("Ink");
                        Check(darkest > ink.R + ink.G + ink.B + 60, "выносная темна, как контур: " + line.Key + ", сумма " + darkest);
                    }
                }

                Check(measured >= 4, "выносных измерено " + measured);
            }
        }

        /// <summary>Вторичные размеры на шаблонах в окнах умолчания (поля 280×427 и 280×646): на «Детекторе» не теряется ни один.</summary>
        static void TemplatesInDefaultWindow()
        {
            int lost = 0, drawings = 0;
            foreach (GeometryPresets.Preset preset in GeometryPresets.Items)
            {
                foreach (GeometryDetectorFacing facing in new[] { GeometryDetectorFacing.Front, GeometryDetectorFacing.Side })
                {
                    GeometryModel m = Preset(preset.Name, facing);
                    if (facing == GeometryDetectorFacing.Side && m.Shape != CrystalShape.Box)
                    {
                        continue;
                    }

                    foreach (Size size in new[] { new Size(280, 427), new Size(280, 646) })
                    {
                        GeometrySketch s = Sketch(m, Tab.Detector, size);
                        using (Paint(s))
                        {
                            drawings++;
                            string where = preset.Name + " " + facing + " " + size.Width + "×" + size.Height + ": ";
                            Check(s.DimensionLevel == 3, where + "вторичные скрыты уровнем " + s.DimensionLevel);
                            foreach (string hidden in s.HiddenDimensions)
                            {
                                lost++;
                                Check(false, where + "потерян размер " + hidden);
                            }

                            // У RC-110 и всех прочих: числа всех толщин, какие заданы.
                            double[] values = facing == GeometryDetectorFacing.Side
                                ? new[] { m.FrontReflectorThickness, m.FrontGapThickness, m.FrontCladdingThickness, m.SideReflectorThickness, m.SideGapThickness, m.SideCladdingThickness, m.MountingThickness }
                                : new[] { m.SideReflectorThickness, m.SideGapThickness, m.SideCladdingThickness, m.FrontReflectorThickness, m.FrontGapThickness, m.FrontCladdingThickness, m.MountingThickness };
                            foreach (double value in values.Where(x => x > 0.0))
                            {
                                Check(s.Labels.Any(x => x.Text == F(value)), where + "нет числа " + F(value));
                            }
                        }
                    }
                }
            }

            Console.WriteLine("шаблоны в окнах умолчания: чертежей " + drawings + ", потеряно размеров " + lost);
        }

        /// <summary>Заслон раскладки сам по себе: место на линии и на теле занято. На обычных сценах числа туда и не просятся,
        /// поэтому проверка прямая: иначе снятый заслон выжил бы молча.</summary>
        static void CostGuard()
        {
            GeometrySketch s = Sketch(Rc110(GeometryDetectorFacing.Side), Tab.Detector, new Size(798, 807));
            using (Paint(s))
            {
                MethodInfo cost = typeof(GeometrySketch).GetMethod("Cost", Any);
                typeof(GeometrySketch).GetField("costField", Any).SetValue(s, new RectangleF(1f, 1f, 796f, 805f));
                Func<RectangleF, float> at = box => (float)cost.Invoke(s, new object[] { box });
                GeometrySketch.DimLine line = s.DimensionLines.First(x => x.Kind == GeometrySketch.DimLineKind.Dimension && !x.OnTopView && x.Side == GeometrySketch.DimSide.Top);
                PointF mid = new PointF(0.5f * (line.A.X + line.B.X), line.A.Y);
                RectangleF labelFree = new RectangleF(mid.X - 60f, mid.Y - 3f, 20f, 6f);
                Check(at(labelFree) > 0f, "заслон: место на размерной линии считается свободным");
                RectangleF c = s.SideContour;
                Check(at(new RectangleF(c.X + 0.5f * c.Width, c.Y + 0.5f * c.Height, 20f, 12f)) > 0f, "заслон: место на теле считается свободным");
                Check(at(new RectangleF(c.X - 500f, c.Y, 20f, 12f)) > 0f, "заслон: место за полем считается свободным");
            }
        }

        /// <summary>Случайные модели: то же правило на геометриях, которых нет в шаблонах. Зерно постоянное.</summary>
        static void RandomScenes()
        {
            Random r = new Random(307);
            Func<double, double, double> u = (lo, hi) => Math.Round(Math.Exp(Math.Log(lo) + r.NextDouble() * (Math.Log(hi) - Math.Log(lo))), 2);
            int drawings = 0, hiddenMain = 0, noPlan = 0;
            for (int i = 0; i < 500; i++)
            {
                GeometryModel g = GeometryEditorPanel.Blank();
                if (r.Next(2) == 0)
                {
                    g.Shape = CrystalShape.Box;
                    g.CrystalBoxX = u(5.0, 150.0);
                    g.CrystalBoxY = u(5.0, 150.0);
                    g.CrystalBoxZ = u(5.0, 150.0);
                    g.Facing = r.Next(2) == 0 ? GeometryDetectorFacing.Side : GeometryDetectorFacing.Front;
                }
                else
                {
                    g.CrystalDiameter = u(5.0, 150.0);
                    g.CrystalHeight = u(5.0, 150.0);
                }

                g.FrontReflectorThickness = u(0.05, 3.0);
                g.SideReflectorThickness = u(0.05, 3.0);
                g.FrontGapThickness = u(0.05, 3.0);
                g.SideGapThickness = u(0.05, 3.0);
                g.FrontCladdingThickness = u(0.1, 5.0);
                g.SideCladdingThickness = u(0.1, 5.0);
                g.MountingThickness = u(0.1, 10.0);
                Func<GeometryModel, GeometryModel> source = Sources[r.Next(5)].Value;
                source(g);
                Tab tab = (Tab)r.Next(2);
                Size size = FieldSizes[r.Next(FieldSizes.Length)];
                GeometrySketch s = Sketch(g, tab, size);
                string where = "случайная " + i + " " + tab + " " + size.Width + "×" + size.Height;
                using (Paint(s))
                {
                    drawings++;
                    if (s.TopViewBounds.IsEmpty)
                    {
                        noPlan++;
                        Console.WriteLine("  без плана: " + where + ", " + g.Shape + " " + F(g.CrystalBoxX) + "×" + F(g.CrystalBoxY) + "×" + F(g.CrystalBoxZ)
                                          + " ⌀" + F(g.CrystalDiameter) + "×" + F(g.CrystalHeight) + " " + g.Facing + ", " + g.SourceType
                                          + ", масштаб " + Field<double>(s, "scale").ToString("F3", CultureInfo.InvariantCulture) + ", запас плана " + Num(Field<double>(s, "planRadiusX")) + "×" + Num(Field<double>(s, "planRadiusY")));
                    }

                    foreach (string line in Gost(LayoutOf(s)).Take(2))
                    {
                        Check(false, where + ": " + line);
                    }

                    Rows(s, where);
                    Rectangle a = s.SideViewBounds, b = s.TopViewBounds;
                    if (!a.IsEmpty)
                    {
                        foreach (GeometrySketch.SketchLabel label in s.Labels)
                        {
                            Check(Whole(RectangleF.Intersect(label.Bounds, a), label.Bounds) || Whole(RectangleF.Intersect(label.Bounds, b), label.Bounds),
                                  where + ": подпись вне своей рамки: " + label.Text);
                        }
                    }

                    hiddenMain += s.HiddenDimensions.Count(x => x.EndsWith("ранг 1)", StringComparison.Ordinal));
                }
            }

            Console.WriteLine("случайные модели: чертежей " + drawings + ", без плана " + noPlan + ", главных размеров скрыто " + hiddenMain);

            // Не отказ: заказанная сетка покрыта целиком, порог плана решает Amber. В журнал числом.
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            string cover = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--cover=", StringComparison.Ordinal))
                {
                    cover = a.Substring(8);
                }
                else
                {
                    Console.Error.WriteLine("неизвестный ключ: " + a);
                    return 2;
                }
            }

            // (`AMBER325`) Главный конфиг пробы — во временной копии: галка пишет его на диск.
            ConfigCopy();
            DashControl();
            OldLayoutRefused();
            TurnedControl();
            Coverage(cover);
            AllNumbers();
            Rebuild();
            Highlight();
            Captions("en", "Rotated: the sample faces the wide side of the crystal", "View from the sample side", "View from the detector side", "From the sample side", "Side view", "Depth: 15");
            Captions("ru", "Развёрнут: проба у широкой грани кристалла", "Вид со стороны пробы", "Вид со стороны детектора", "Со стороны пробы", "Вид сбоку", "Глубина: 15");
            Plans();
            PlansFromDetector();
            SideCheck();
            SideMemory();
            Overview();
            ModelUntouched();
            CriticScene();
            GuideLines();
            TemplatesInDefaultWindow();
            CostGuard();
            ShortRoutes();
            RandomScenes();
            Console.WriteLine("галка (`SideCheck`): " + string.Join(", ", KindSeen.OrderBy(x => x.Key).Select(x => x.Key + " " + x.Value)) + "; мера среза — галок " + ClipSeen);
            ConfigCopyDrop();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ: проверок " + checks : "РАСХОЖДЕНИЙ " + bad + " из " + checks);
            return bad == 0 ? 0 : 1;
        }
    }
}
