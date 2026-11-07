using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
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
                    foreach (GeometrySketch.DimLine d in dims)
                    {
                        float at = h ? d.A.Y : d.A.X;
                        float off = side == GeometrySketch.DimSide.Top ? contour.Top - at
                                  : side == GeometrySketch.DimSide.Bottom ? at - contour.Bottom
                                  : side == GeometrySketch.DimSide.Left ? contour.Left - at
                                  : at - contour.Right;
                        double row = (off - GeometrySketch.DimFirstPx) / GeometrySketch.DimPitchPx;
                        Check(Math.Abs(row - d.Row) < 0.05, where + ": ряд " + d.Row + " не на шаге: " + off.ToString("F1", CultureInfo.InvariantCulture) + " точек от контура");
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
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("поле", Iso),
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("земля", Ground),
            new KeyValuePair<string, Func<GeometryModel, GeometryModel>>("лунка", Borehole),
        };

        static void Coverage(string coverFile)
        {
            List<string> report = new List<string>();
            int total = 0, noPlan = 0, shieldTab = 0, hiddenMain = 0, hiddenShield = 0, hiddenTight = 0, leaderCross = 0, leaderCrossShield = 0;
            Dictionary<int, int> levels = new Dictionary<int, int>();
            Action<string, Tab, Func<GeometryModel>, Size, bool> one = (name, tab, make, size, ordered) =>
            {
                string where = name + " " + tab + " " + size.Width + "×" + size.Height;
                GeometrySketch s = Sketch(make(), tab, size);
                using (Paint(s))
                {
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
                    one(crystal.Key, Tab.Detector, crystal.Value, size, ordered);
                    foreach (var source in Sources)
                    {
                        KeyValuePair<string, Func<GeometryModel>> c = crystal;
                        KeyValuePair<string, Func<GeometryModel, GeometryModel>> z = source;
                        one(c.Key + " + " + z.Key, Tab.Source, () => z.Value(c.Value()), size, ordered);
                    }

                    KeyValuePair<string, Func<GeometryModel>> c2 = crystal;
                    one(c2.Key + " в защите, точка", Tab.Shield, () => Shielded(Point(c2.Value())), size, ordered);
                    one(c2.Key + " в защите, маринелли", Tab.Shield, () => Shielded(Marinelli(c2.Value())), size, ordered);
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
            Check(hiddenMain == 0, "главных размеров скрыто " + hiddenMain);
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
            LitOn("маринелли колодец", Marinelli(NaI()), Tab.Source, "MarinelliHoleDiameter", true, true);
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

        static void Captions(string culture, string facing, string topTitle, string sideTitle, string depthWord)
        {
            Resources.Culture = new CultureInfo(culture);
            Check(Resources.EfficiencySketchFacingSide == facing, culture + ": строка разворота: " + Resources.EfficiencySketchFacingSide);
            foreach (Tab tab in new[] { Tab.Detector, Tab.Source })
            {
                foreach (Size size in FieldSizes.Concat(MoreSizes))
                {
                    string where = culture + " " + tab + " " + size.Width + ": ";
                    GeometrySketch s = Sketch(Point(Nano(GeometryDetectorFacing.Side)), tab, size);
                    using (Paint(s))
                    {
                        string text = s.TopViewBounds.IsEmpty ? string.Join(" ", s.Labels.Select(l => l.Text)) : Joined(s, s.TopViewBounds);
                        if (!s.TopViewBounds.IsEmpty)
                        {
                            Check(text.StartsWith(topTitle, StringComparison.Ordinal), where + "заголовок плана: " + text);
                            Check(Joined(s, s.SideViewBounds).StartsWith(sideTitle, StringComparison.Ordinal), where + "заголовок разреза");
                        }

                        Check(text.Contains(facing), where + "нет подписи разворота: " + text);
                        Check(text.Contains(depthWord), where + "нет глубины: " + text);
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

        static GeometrySketch.SketchLabel[] Top(GeometryModel m, out GeometrySketch s)
        {
            s = Sketch(m, Tab.Source, new Size(798, 807));
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

        static void Plans()
        {
            Color wall = Palette("WallColor"), sampleColor = Palette("SampleColor"), canvas = Palette("Canvas"), crystalColor = Palette("CrystalColor"), inkColor = Palette("Ink");
            GeometrySketch s;

            // Маринелли: кольцо пробы, стенки сосуда и колодца, кристалл поверх.
            GeometryModel mar = Marinelli(NaI());
            mar.MarinelliSideThickness = 8.0;
            mar.MarinelliHoleSideThickness = 6.0;
            GeometrySketch.SketchLabel[] t = Top(mar, out s);
            Has(t, "маринелли", "MarinelliBeakerDiameter", 130.0);
            Has(t, "маринелли", "MarinelliHoleDiameter", 76.0);
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                Check(OnCircle(b, c, 0.5 * (44.0 + 57.0) * scale, sampleColor) > 600, "маринелли: проба не в кольце между колодцем и сосудом");
                Check(OnCircle(b, c, (65.0 - 4.0) * scale, wall) > 600, "маринелли: нет стенки сосуда");
                Check(OnCircle(b, c, (38.0 + 3.0) * scale, wall) > 600, "маринелли: нет стенки колодца");
                Check(Near(b.GetPixel((int)c.X, (int)c.Y), crystalColor), "маринелли: кристалл не поверх: вид со стороны детектора");
            }

            // Банка: стенка и проба.
            GeometryModel cyl = Cylinder(NaI());
            cyl.BeakerDiameter = 150.0;
            cyl.BeakerSideWallThickness = 8.0;
            t = Top(cyl, out s);
            Has(t, "банка", "BeakerDiameter", 150.0);
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                Check(OnCircle(b, c, (75.0 - 4.0) * scale, wall) > 600, "банка: нет стенки");
                Check(OnCircle(b, c, (75.0 - 14.0) * scale, sampleColor) > 600, "банка: нет пробы");
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

            // Трапеция: основание 1 при y < 0. Ось Y вверх: снизу узко (80), сверху широко (100).
            t = Top(Trapezoid(NaI()), out s);
            Has(t, "трапеция", "MarinelliHoleBase1", 80.0);
            Has(t, "трапеция", "MarinelliHoleBase2", 100.0);
            Has(t, "трапеция", "MarinelliHoleWidth", 76.0);
            Check(!t.Any(l => l.Key == "MarinelliHoleDiameter"), "трапеция: на плане диаметр колодца");
            Check(t.Any(l => l.Key == "MarinelliHoleBase2" && l.Bounds.Bottom < s.TopContour.Top), "трапеция: основание 2 не сверху");
            Check(t.Any(l => l.Key == "MarinelliHoleBase1" && l.Bounds.Top > s.TopContour.Bottom - 40f), "трапеция: основание 1 не снизу");
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                int upper = OnRow(b, s.TopViewBounds, (int)(c.Y - 34.0 * scale), canvas);
                int lower = OnRow(b, s.TopViewBounds, (int)(c.Y + 34.0 * scale), canvas);
                int outside = OnRow(b, s.TopViewBounds, (int)(c.Y), canvas);
                Check(upper > lower + 5.0 * scale, "трапеция перевёрнута: сверху просвет " + upper + ", снизу " + lower + " точек");
                Check(outside > 0, "трапеция: нет просвета");
            }

            // Точка: штриховой кружок поверх кристалла, не сплошной.
            t = Top(Point(Nano(GeometryDetectorFacing.Side)), out s);
            double hx, hy, depth;
            string kx, ky, kd;
            Nano(GeometryDetectorFacing.Side).CrystalBoxInScene(out hx, out hy, out depth, out kx, out ky, out kd);
            Has(t, "точка брусок", kx, 18.0);
            Has(t, "точка брусок", ky, 60.0);
            using (Bitmap b = Paint(s))
            {
                PointF c = Field<PointF>(s, "planCenter");
                int dark = OnCircle(b, c, 5.0, inkColor);
                Check(dark > 60 && dark < 600, "точка: нет штрихового кружка: тёмных точек " + dark + " из 720");
            }

            // Поле: условная окружность.
            t = Top(Iso(Rc110(GeometryDetectorFacing.Side)), out s);
            using (Bitmap b = Paint(s))
            {
                double scale = Field<double>(s, "scale");
                PointF c = Field<PointF>(s, "planCenter");
                double radius = Field<double>(s, "planRadiusX") * scale;
                Check(OnCircle(b, c, radius, sampleColor) > 100, "поле: нет кольца");
            }
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

            OldLayoutRefused();
            Coverage(cover);
            AllNumbers();
            Rebuild();
            Highlight();
            Captions("en", "Rotated: the sample faces the wide side of the crystal", "Top view", "Side view", "Depth: 15");
            Captions("ru", "Развёрнут: проба у широкой грани кристалла", "Вид сверху", "Вид сбоку", "Глубина: 15");
            Plans();
            Overview();
            ModelUntouched();
            CriticScene();
            GuideLines();
            TemplatesInDefaultWindow();
            CostGuard();
            ShortRoutes();
            RandomScenes();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ: проверок " + checks : "РАСХОЖДЕНИЙ " + bad + " из " + checks);
            return bad == 0 ? 0 : 1;
        }
    }
}
